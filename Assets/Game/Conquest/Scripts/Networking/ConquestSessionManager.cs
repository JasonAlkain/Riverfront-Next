using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Game.Conquest.Gameplay;
using Game.Conquest.Maps;
using Game.Conquest.Scenes;

namespace Game.Conquest.Networking
{
    /// <summary>LAN / direct IP session backend over Unity Transport.</summary>
    public class ConquestSessionManager : MonoBehaviour, IConquestSessionService
    {
        public static ConquestSessionManager Instance { get; private set; }

        [Header("Active Session Configuration")]
        public string sessionName = "Conquest Session";
        public string selectedMapId = "the_frontline";
        public string selectedMapScene = "ConquestLargeBattlefield";
        public int maxHumanPlayers = 16;
        [Tooltip("Rules for the next match. Set by the menus, the lobby or the server launch args; read by the match.")]
        public ConquestMatchSettings matchSettings = ConquestMatchSettings.Default;
        public bool isSoloMode = false;

        [Header("Dedicated Server")]
        [Tooltip("Set at runtime when this process was launched as a headless server (no local player).")]
        public bool isDedicatedServer = false;
        [Tooltip("Dedicated server: humans required (all ready) before the auto-start countdown begins.")]
        public int minPlayersToStart = 2;
        [Tooltip("Seconds between everyone in the lobby being ready and the map loading (host or dedicated server).")]
        public float autoStartCountdownSeconds = 10f;
        public string connectAddress = "127.0.0.1";
        public ushort connectPort = DefaultPort;
        [Tooltip("Port a listen-server host binds to.")]
        public ushort hostPort = DefaultPort;

        [Header("Client Connection")]
        [Tooltip("How long a client keeps trying to reach a server before giving up.")]
        [SerializeField] private float clientConnectTimeoutSeconds = 10f;

        public const ushort DefaultPort = 7777;

        private string m_StatusMessage = "Ready (LAN / Direct IP)";

        public string StatusMessage => m_StatusMessage;

        public void SetStatus(string message)
        {
            m_StatusMessage = message ?? "";
            OnStatusChanged?.Invoke(m_StatusMessage);
        }

        public event Action<string> OnStatusChanged;

        /// <summary>Lobby choices committed by the server when the match starts.</summary>
        public struct PlayerAssignment
        {
            public Team team;
            public int classIndex;
            public string playerName;
        }

        // Server-side only. Lives here (DontDestroyOnLoad) because the lobby manager is a
        // MainMenu scene object and is destroyed when the map loads.
        private readonly Dictionary<ulong, PlayerAssignment> m_PlayerAssignments = new Dictionary<ulong, PlayerAssignment>();

        public const int MaxPlayerNameLength = 24;

        public void SetPlayerAssignment(ulong clientId, PlayerAssignment assignment)
        {
            m_PlayerAssignments[clientId] = assignment;
        }

        public bool TryGetPlayerAssignment(ulong clientId, out PlayerAssignment assignment)
        {
            return m_PlayerAssignments.TryGetValue(clientId, out assignment);
        }

        public void ClearPlayerAssignments()
        {
            m_PlayerAssignments.Clear();
        }

        /// <summary>Trims, strips rich-text brackets and limits length. Returns empty if nothing usable remains.</summary>
        public static string SanitizePlayerName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw.Trim())
            {
                if (c == '<' || c == '>' || char.IsControl(c) || char.IsSurrogate(c)) continue;
                sb.Append(c);
                if (sb.Length >= MaxPlayerNameLength) break;
            }
            // Names are replicated as FixedString64Bytes (61 UTF-8 bytes max); assigning more throws.
            while (sb.Length > 0 && System.Text.Encoding.UTF8.GetByteCount(sb.ToString()) > 60)
            {
                sb.Length--;
            }
            return sb.ToString().Trim();
        }

        // ---------- Connection approval ----------

        /// <summary>Bump when the network protocol changes incompatibly (RPCs, NetworkVariables, prefab list).</summary>
        public const string ProtocolVersion = "2";
        public static string BuildVersion => $"{Application.version}/p{ProtocolVersion}";

        private const string PayloadTag = "RF";

        [Header("Connection Approval")]
        [Tooltip("When false the server refuses new connections (e.g. a match that does not allow late joiners).")]
        public bool acceptingJoins = true;

        // Server-side: names supplied in each client's connection payload, keyed by client id.
        private readonly Dictionary<ulong, string> m_ApprovedNames = new Dictionary<ulong, string>();

        // The name this game instance chose in the lobby. Kept in memory for this run only:
        // instances on one machine share PlayerPrefs, so a saved name made every new client join
        // as whoever typed a name last. Empty means the server gives a generic one (Player_N).
        private static string s_LocalPlayerName = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLocalPlayerName()
        {
            s_LocalPlayerName = "";
        }

        public static string LoadLocalPlayerName()
        {
            return s_LocalPlayerName;
        }

        public static void SaveLocalPlayerName(string name)
        {
            s_LocalPlayerName = SanitizePlayerName(name);
        }

        public bool TryGetApprovedName(ulong clientId, out string playerName)
        {
            return m_ApprovedNames.TryGetValue(clientId, out playerName) && !string.IsNullOrEmpty(playerName);
        }

        /// <summary>
        /// Enables approval on the NetworkManager and sets this peer's outgoing payload. Must run before
        /// StartHost/StartServer/StartClient. The payload is a plain string so a Steam auth ticket can
        /// later be added as another field without changing the approval seam.
        /// </summary>
        [Header("Network Prefabs")]
        [Tooltip("Runtime-spawned prefabs (bots, support stations, player). Registered on the single persistent " +
                 "NetworkManager before every start, whichever scene's copy that is.")]
        [SerializeField] private NetworkPrefabsList networkPrefabs;

        [Header("Network Config")]
        [Tooltip("Network ticks per second. Part of NGO's config check, so every peer must use the same value; " +
                 "it is applied here on every start rather than taken from whichever scene's NetworkManager survived.")]
        [SerializeField] private uint tickRate = 30;

        /// <summary>Everything that must be set on the NetworkManager before StartHost/StartServer/StartClient.</summary>
        private void PrepareNetworkManager(NetworkManager nm)
        {
            nm.NetworkConfig.TickRate = tickRate;
            // Server-to-client log forwarding and per-message metrics cost bandwidth and CPU;
            // keep them for the Editor and development builds only.
            bool debugBuild = Debug.isDebugBuild;
            nm.NetworkConfig.EnableNetworkLogs = debugBuild;
            nm.NetworkConfig.NetworkMessageMetrics = debugBuild;
            nm.NetworkConfig.NetworkProfilingMetrics = debugBuild;

            EnsureNetworkPrefabsRegistered(nm);
            // Named-message handlers only exist once the client has started.
            nm.OnClientStarted -= RegisterResultsMessageHandler;
            nm.OnClientStarted += RegisterResultsMessageHandler;
            nm.NetworkConfig.AutoSpawnPlayerPrefabClientSide = false;
            ConfigureConnectionApproval(nm);
        }

        private void EnsureNetworkPrefabsRegistered(NetworkManager nm)
        {
            if (networkPrefabs == null)
            {
                Debug.LogWarning("[ConquestSessionManager] No network prefab list assigned; bots and stations may not replicate.");
                return;
            }
            foreach (var entry in networkPrefabs.PrefabList)
            {
                if (entry == null || entry.Prefab == null) continue;
                if (nm.NetworkConfig.Prefabs.Contains(entry.Prefab)) continue;
                nm.AddNetworkPrefab(entry.Prefab);
            }
        }

        private void ConfigureConnectionApproval(NetworkManager nm)
        {
            m_ApprovedNames.Clear();
            nm.NetworkConfig.ConnectionApproval = true;
            nm.ConnectionApprovalCallback = HandleConnectionApproval;
            string payload = $"{PayloadTag}|{BuildVersion}|{LoadLocalPlayerName()}";
            nm.NetworkConfig.ConnectionData = System.Text.Encoding.UTF8.GetBytes(payload);
        }

        private void HandleConnectionApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            // Players are spawned by ConquestNetworkManager once the client has loaded the map,
            // not on connect (which would put them in the MainMenu lobby).
            response.CreatePlayerObject = false;
            response.Pending = false;

            string version = "";
            string requestedName = "";
            if (request.Payload != null && request.Payload.Length > 0 && request.Payload.Length <= 256)
            {
                string[] parts = System.Text.Encoding.UTF8.GetString(request.Payload).Split(new[] { '|' }, 3);
                if (parts.Length >= 2 && parts[0] == PayloadTag) version = parts[1];
                if (parts.Length == 3) requestedName = parts[2];
            }

            var nm = NetworkManager.Singleton;
            bool isLocalHostClient = nm != null && request.ClientNetworkId == NetworkManager.ServerClientId;

            if (!isLocalHostClient)
            {
                if (version != BuildVersion)
                {
                    Reject(response, request.ClientNetworkId, string.IsNullOrEmpty(version)
                        ? "Version mismatch: the server did not recognise your game version."
                        : $"Version mismatch: server is {BuildVersion}, you have {version}.");
                    return;
                }

                if (!acceptingJoins)
                {
                    Reject(response, request.ClientNetworkId, "This server is not accepting new players right now.");
                    return;
                }

                // ConnectedClientsIds includes the host's own client on a listen server.
                int humans = nm != null ? nm.ConnectedClientsIds.Count : 0;
                if (humans >= Mathf.Max(1, maxHumanPlayers))
                {
                    Reject(response, request.ClientNetworkId, $"Server full ({maxHumanPlayers}/{maxHumanPlayers} players).");
                    return;
                }
            }

            string clean = SanitizePlayerName(requestedName);
            if (!string.IsNullOrEmpty(clean)) m_ApprovedNames[request.ClientNetworkId] = clean;
            response.Approved = true;
        }

        private static void Reject(NetworkManager.ConnectionApprovalResponse response, ulong clientId, string reason)
        {
            Debug.Log($"[ConquestSessionManager] Rejected client {clientId}: {reason}");
            response.Approved = false;
            response.CreatePlayerObject = false;
            response.Reason = reason;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            ConquestSession.Current = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            if (ReferenceEquals(ConquestSession.Current, this)) ConquestSession.Current = null;
        }

        public void StartSoloGame(string mapSceneName, int tickets, int teamSize, BotDifficulty difficulty)
        {
            m_OnlineAttempt++;
            m_OnlineJoinCode = "";
            isSoloMode = true;
            ClearPlayerAssignments();
            sessionName = "Solo Practice";
            matchSettings = new ConquestMatchSettings
            {
                reinforcementTickets = tickets,
                teamSize = teamSize,
                botFilling = true,
                botDifficulty = difficulty,
            };

            // Solo stays on loopback; set explicitly because the transport persists between sessions.
            EnsureNetworkManagerConfigured("127.0.0.1", DefaultPort, "127.0.0.1");

            if (NetworkManager.Singleton != null)
            {
                if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
                {
                    NetworkManager.Singleton.Shutdown();
                }

                PrepareNetworkManager(NetworkManager.Singleton);
                NetworkManager.Singleton.StartHost();
                ConquestSceneTransitionManager.Instance?.TransitionToMap(mapSceneName, true);
            }
        }

        // Must be stored before StartHost(): the lobby reads these in OnNetworkSpawn.
        private void ConfigureHost(string mapId, string mapSceneName, string sessName, int maxPlayers, int tickets, int teamSize, bool botFill, BotDifficulty diff)
        {
            isSoloMode = false;
            ClearPlayerAssignments();
            selectedMapId = mapId;
            selectedMapScene = mapSceneName;
            sessionName = sessName;
            maxHumanPlayers = maxPlayers;
            matchSettings = new ConquestMatchSettings
            {
                reinforcementTickets = tickets,
                teamSize = teamSize,
                botFilling = botFill,
                botDifficulty = diff,
            };
        }

        public bool StartHostGame(string mapId, string mapSceneName, string sessName, int maxPlayers, int tickets, int teamSize, bool botFill, BotDifficulty diff)
        {
            m_OnlineAttempt++;
            m_OnlineJoinCode = "";
            ConfigureHost(mapId, mapSceneName, sessName, maxPlayers, tickets, teamSize, botFill, diff);

            // Listen on all interfaces so LAN players can reach the host (loopback-only refuses them).
            EnsureNetworkManagerConfigured("127.0.0.1", hostPort, AnyListenAddress);

            if (NetworkManager.Singleton != null)
            {
                if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
                {
                    NetworkManager.Singleton.Shutdown();
                }

                PrepareNetworkManager(NetworkManager.Singleton);
                bool success = NetworkManager.Singleton.StartHost();
                if (success)
                {
                    m_StatusMessage = $"Hosting on {GetHostAddressSummary(hostPort)}";
                    Debug.Log($"[ConquestSessionManager] {m_StatusMessage}");
                    OnStatusChanged?.Invoke(m_StatusMessage);
                    return true;
                }
            }

            m_StatusMessage = "Failed to start Host session";
            OnStatusChanged?.Invoke(m_StatusMessage);
            return false;
        }

        /// <summary>
        /// Dedicated-server entry point: no local player. Call once the MainMenu scene (which holds
        /// the lobby NetworkObject) is loaded; the lobby then auto-starts the match when ready.
        /// </summary>
        public void StartDedicatedServer(ConquestServerLaunchArgs args)
        {
            StartCoroutine(StartDedicatedServerNextFrame(args));
        }

        // Wait a frame so the freshly loaded scene's NetworkManager and in-scene NetworkObjects
        // have finished Awake/Start before the server spawns them.
        private System.Collections.IEnumerator StartDedicatedServerNextFrame(ConquestServerLaunchArgs args)
        {
            yield return null;
            if (!TryStartDedicatedServer(args))
            {
                Debug.LogError("[ConquestSessionManager] Dedicated server failed to start; quitting.");
                Application.Quit(1);
            }
        }

        private bool TryStartDedicatedServer(ConquestServerLaunchArgs args)
        {
            isSoloMode = false;
            isDedicatedServer = true;
            ClearPlayerAssignments();

            if (args != null)
            {
                if (!string.IsNullOrEmpty(args.mapId))
                {
                    selectedMapId = args.mapId;
                    selectedMapScene = ""; // resolved from the registry by the lobby
                }
                if (!string.IsNullOrEmpty(args.sessionName)) sessionName = args.sessionName;
                if (args.port.HasValue) hostPort = args.port.Value;
                if (args.maxPlayers.HasValue) maxHumanPlayers = args.maxPlayers.Value;
                if (args.tickets.HasValue) matchSettings.reinforcementTickets = args.tickets.Value;
                if (args.teamSize.HasValue) matchSettings.teamSize = args.teamSize.Value;
                if (args.bots.HasValue) matchSettings.botFilling = args.bots.Value;
                if (args.botDifficulty.HasValue) matchSettings.botDifficulty = args.botDifficulty.Value;
                if (args.minPlayersToStart.HasValue) minPlayersToStart = args.minPlayersToStart.Value;
                if (args.startCountdownSeconds.HasValue) autoStartCountdownSeconds = args.startCountdownSeconds.Value;
            }

            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                Debug.LogError("[ConquestSessionManager] Dedicated server: no NetworkManager in the loaded scene.");
                return false;
            }

            EnsureNetworkManagerConfigured("127.0.0.1", hostPort, AnyListenAddress);
            PrepareNetworkManager(nm);

            if (!nm.StartServer())
            {
                Debug.LogError($"[ConquestSessionManager] Dedicated server failed to start on port {hostPort}.");
                return false;
            }

            SetStatus($"Dedicated server '{sessionName}' on {GetHostAddressSummary(hostPort)} | map {selectedMapId} | max {maxHumanPlayers} players");
            Debug.Log($"[ConquestSessionManager] {StatusMessage}");
            return true;
        }

        public bool StartClientConnect(ConquestConnectionData connection)
        {
            isSoloMode = false;
            m_JoinedCode = "";
            if (connection.IsOnline)
            {
                JoinOnline(ConquestOnlineServices.NormalizeJoinCode(connection.joinCode));
                return NetworkManager.Singleton != null;
            }
            connectAddress = string.IsNullOrEmpty(connection.address) ? "127.0.0.1" : connection.address;
            connectPort = connection.port > 0 ? connection.port : DefaultPort;

            EnsureNetworkManagerConfigured(connectAddress, connectPort);
            ApplyClientConnectTimeout();

            if (NetworkManager.Singleton != null)
            {
                if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
                {
                    NetworkManager.Singleton.Shutdown();
                }

                PrepareNetworkManager(NetworkManager.Singleton);
                bool success = NetworkManager.Singleton.StartClient();
                if (success)
                {
                    m_StatusMessage = $"Connecting to {connectAddress}:{connectPort}...";
                    OnStatusChanged?.Invoke(m_StatusMessage);
                    return true;
                }
            }

            m_StatusMessage = "Failed to start client connection";
            OnStatusChanged?.Invoke(m_StatusMessage);
            return false;
        }

        // Online join: get the relay for the code, then connect as normal. A cancel while the relay
        // is still being joined bumps the attempt number, so the late result is dropped.
        private async void JoinOnline(string joinCode)
        {
            int attempt = ++m_OnlineAttempt;
            var nm = NetworkManager.Singleton;
            var transport = nm != null ? nm.GetComponent<UnityTransport>() : null;
            if (transport == null) return;

            if (nm.IsClient || nm.IsServer) nm.Shutdown();
            SetStatus($"Finding online match {joinCode}...");
            try
            {
                await ConquestOnlineServices.JoinRelayAsync(transport, joinCode);
            }
            catch (Exception e)
            {
                if (attempt != m_OnlineAttempt) return;
                Debug.LogWarning($"[ConquestSessionManager] Online join failed: {e}");
                SetStatus(ConquestOnlineServices.Describe(e));
                OnOnlineJoinFailed?.Invoke(ConquestOnlineServices.Describe(e));
                return;
            }
            if (attempt != m_OnlineAttempt) return;

            m_JoinedCode = joinCode;
            ApplyClientConnectTimeout();
            PrepareNetworkManager(nm);
            if (nm.StartClient())
            {
                SetStatus($"Connecting to online match {joinCode}...");
            }
            else
            {
                SetStatus("Failed to start client connection");
                OnOnlineJoinFailed?.Invoke(StatusMessage);
            }
        }

        /// <summary>An online join failed before a connection was even attempted (bad code, no internet).</summary>
        public event Action<string> OnOnlineJoinFailed;

        public void StartOnlineHostGame(string mapId, string mapSceneName, string sessName, int maxPlayers, int tickets, int teamSize, bool botFill, BotDifficulty diff, Action<bool, string> done)
        {
            HostOnline(mapId, mapSceneName, sessName, maxPlayers, tickets, teamSize, botFill, diff, done);
        }

        private async void HostOnline(string mapId, string mapSceneName, string sessName, int maxPlayers, int tickets, int teamSize, bool botFill, BotDifficulty diff, Action<bool, string> done)
        {
            int attempt = ++m_OnlineAttempt;
            var nm = NetworkManager.Singleton;
            var transport = nm != null ? nm.GetComponent<UnityTransport>() : null;
            if (transport == null)
            {
                done?.Invoke(false, "No network manager.");
                return;
            }
            if (nm.IsClient || nm.IsServer) nm.Shutdown();

            SetStatus("Creating online match...");
            string joinCode;
            try
            {
                // The host is not one of the relay's connections; everyone else is.
                joinCode = await ConquestOnlineServices.CreateRelayAsync(transport, Mathf.Max(1, maxPlayers - 1));
            }
            catch (Exception e)
            {
                if (attempt != m_OnlineAttempt) return;
                Debug.LogWarning($"[ConquestSessionManager] Online host failed: {e}");
                SetStatus(ConquestOnlineServices.Describe(e));
                done?.Invoke(false, StatusMessage);
                return;
            }
            if (attempt != m_OnlineAttempt) return;

            ConfigureHost(mapId, mapSceneName, sessName, maxPlayers, tickets, teamSize, botFill, diff);
            PrepareNetworkManager(nm);
            if (!nm.StartHost())
            {
                SetStatus("Failed to start Host session");
                done?.Invoke(false, StatusMessage);
                return;
            }
            m_OnlineJoinCode = joinCode;
            SetStatus($"Hosting online match, join code {joinCode}");
            Debug.Log($"[ConquestSessionManager] {StatusMessage}");
            done?.Invoke(true, StatusMessage);
        }

        // Join code of the online match this peer hosts; empty when hosting on LAN or not hosting.
        private string m_OnlineJoinCode = "";
        // Bumped by every online start and by cancel/leave, so a stale async result is ignored.
        private int m_OnlineAttempt;

        // Join code of the online match this client joined; empty for LAN clients and hosts.
        private string m_JoinedCode = "";

        public bool TryGetShareInfo(out string label, out string shown, out string copyText)
        {
            label = shown = copyText = "";
            var nm = NetworkManager.Singleton;
            if (nm == null || isSoloMode || !(nm.IsServer || nm.IsClient)) return false;
            if (nm.IsServer)
            {
                if (IsHostingOnline)
                {
                    label = "JOIN CODE";
                    shown = copyText = m_OnlineJoinCode;
                    return true;
                }
                var ips = GetLocalIPv4Addresses();
                if (ips.Count == 0) return false;
                label = "LAN ADDRESS";
                shown = $"{ips[0]}:{hostPort}";
                copyText = GetHostAddressSummary(hostPort);
                return true;
            }
            if (string.IsNullOrEmpty(m_JoinedCode)) return false;
            label = "JOIN CODE";
            shown = copyText = m_JoinedCode;
            return true;
        }

        /// <summary>True while hosting an online (Relay) match rather than a LAN one.</summary>
        public bool IsHostingOnline => !string.IsNullOrEmpty(m_OnlineJoinCode);

        public void CancelConnect()
        {
            m_OnlineAttempt++;
            m_JoinedCode = "";
            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsClient || nm.IsServer)) nm.Shutdown();
            SetStatus("Connection cancelled");
        }

        public string HostJoinAddress => IsHostingOnline ? $"JOIN CODE: {m_OnlineJoinCode}" : $"PLAYERS JOIN AT: {GetHostAddressSummary(hostPort)}";

        // ---------- Match flow and results ----------

        [Header("Match Flow")]
        [Tooltip("Scene shown between a finished match and the lobby. Must be in the build scene list.")]
        public string resultsSceneName = "MatchResults";
        [Tooltip("Scene that holds the lobby. Players return here (still connected) after the results.")]
        public string lobbySceneName = "MainMenu";
        [Tooltip("Seconds the in-map VICTORY/DEFEAT banner stays up before the results scene loads.")]
        public float victoryBannerSeconds = 6f;
        [Tooltip("Seconds the results screen stays up before everyone returns to the lobby.")]
        public float resultsScreenSeconds = 15f;

        private const string MatchResultsMessage = "Conquest.MatchResults";

        /// <summary>Results of the last finished match on this peer (server: built; client: received).</summary>
        public MatchResults LastMatchResults { get; private set; }

        /// <summary>Raised on every peer when <see cref="LastMatchResults"/> is set.</summary>
        public event Action<MatchResults> OnMatchResultsAvailable;

        /// <summary>Server: store the final results; they are sent to clients when they reach the results scene.</summary>
        public void SetMatchResults(MatchResults results)
        {
            LastMatchResults = results;
            OnMatchResultsAvailable?.Invoke(results);
        }

        public void ClearMatchResults()
        {
            LastMatchResults = null;
        }

        /// <summary>Server: send the stored results to one client.</summary>
        public void SendMatchResultsTo(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || LastMatchResults == null || nm.CustomMessagingManager == null) return;
            if (clientId == NetworkManager.ServerClientId) return; // the host reads LastMatchResults directly

            // Up to 64 rows exceeds one packet, so use the fragmented channel.
            using (var writer = new FastBufferWriter(1024, Unity.Collections.Allocator.Temp, 64 * 1024))
            {
                writer.WriteNetworkSerializable(LastMatchResults);
                nm.CustomMessagingManager.SendNamedMessage(MatchResultsMessage, clientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        private void RegisterResultsMessageHandler()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.CustomMessagingManager == null || nm.IsServer) return;
            nm.CustomMessagingManager.UnregisterNamedMessageHandler(MatchResultsMessage);
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MatchResultsMessage, HandleMatchResultsMessage);
        }

        private void HandleMatchResultsMessage(ulong senderClientId, FastBufferReader reader)
        {
            var results = new MatchResults();
            reader.ReadNetworkSerializable(out results);
            SetMatchResults(results);
        }

        public void LeaveSession()
        {
            m_OnlineAttempt++;
            m_JoinedCode = "";
            m_OnlineJoinCode = "";
            m_StatusMessage = "Leaving session...";
            OnStatusChanged?.Invoke(m_StatusMessage);
            ClearPlayerAssignments();
            ClearMatchResults();

            if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
            {
                NetworkManager.Singleton.Shutdown();
            }

            ConquestSceneTransitionManager.Instance?.ReturnToMainMenu();
        }

        public const string AnyListenAddress = "0.0.0.0";

        /// <param name="address">Address a client connects to (and the host's own client uses).</param>
        /// <param name="listenAddress">Address the server binds to. Empty means "same as address" in UTP,
        /// so pass it explicitly for every role to avoid inheriting a previous session's value.</param>
        public void EnsureNetworkManagerConfigured(string address, ushort port, string listenAddress = "")
        {
            if (NetworkManager.Singleton != null)
            {
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                if (transport != null)
                {
                    // Also switches the transport back from Relay after an online session. Forced so
                    // the -port/-ip command line can't override what the menus or launch args chose.
                    transport.SetConnectionData(true, address, port, listenAddress ?? "");
                }
            }
        }

        // UTP's default is 60 attempts x 1 s, which leaves a player staring at "Connecting..." for a minute.
        private void ApplyClientConnectTimeout()
        {
            var transport = NetworkManager.Singleton != null ? NetworkManager.Singleton.GetComponent<UnityTransport>() : null;
            if (transport == null) return;
            int perAttemptMs = Mathf.Max(250, transport.ConnectTimeoutMS);
            transport.MaxConnectAttempts = Mathf.Max(1, Mathf.CeilToInt(clientConnectTimeoutSeconds * 1000f / perAttemptMs));
        }

        /// <summary>Human-readable "ip:port" list of this machine's LAN IPv4 addresses, for players to type in.</summary>
        public static string GetHostAddressSummary(ushort port)
        {
            var ips = GetLocalIPv4Addresses();
            if (ips.Count == 0) return $"127.0.0.1:{port} (no LAN address found)";
            for (int i = 0; i < ips.Count; i++) ips[i] = $"{ips[i]}:{port}";
            return string.Join(", ", ips);
        }

        public static List<string> GetLocalIPv4Addresses()
        {
            var result = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(ua.Address)) continue;
                        string ip = ua.Address.ToString();
                        if (ip.StartsWith("169.254.")) continue; // link-local, not reachable by other players
                        if (!result.Contains(ip)) result.Add(ip);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ConquestSessionManager] Could not enumerate network interfaces: {ex.Message}");
            }
            return result;
        }
    }
}
