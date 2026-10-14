using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Game.Conquest.Gameplay;
using Game.Conquest.Maps;
using Game.Conquest.Scenes;

namespace Game.Conquest.Networking
{
    public class ConquestSessionManager : MonoBehaviour
    {
        public static ConquestSessionManager Instance { get; private set; }

        [Header("Active Session Configuration")]
        public string sessionName = "Conquest Session";
        public string selectedMapId = "the_frontline";
        public string selectedMapScene = "ConquestLargeBattlefield";
        public int maxHumanPlayers = 16;
        public int reinforcementTickets = 150;
        public int targetTeamSize = 8;
        public bool enableBotFilling = true;
        public BotDifficulty botDifficulty = BotDifficulty.Normal;
        public bool isSoloMode = false;
        public string lastJoinCode = "";
        public string connectAddress = "127.0.0.1";
        public ushort connectPort = 7777;

        [Header("Services Status")]
        private bool m_ServicesInitialized = false;
        private string m_StatusMessage = "Ready";

        public bool ServicesInitialized => m_ServicesInitialized;
        public string StatusMessage => m_StatusMessage;

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

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private Task<bool> m_InitTask;

        private void Start()
        {
            _ = InitializeServicesAsync();
        }

        public Task<bool> InitializeServicesAsync()
        {
            if (m_ServicesInitialized) return Task.FromResult(true);
            if (m_InitTask != null && !m_InitTask.IsCompleted) return m_InitTask;

            m_InitTask = InternalInitializeAsync();
            return m_InitTask;
        }

        private async Task<bool> InternalInitializeAsync()
        {
            try
            {
                m_StatusMessage = "Initializing Gaming Services...";
                OnStatusChanged?.Invoke(m_StatusMessage);

                // Initialize Unity Services Core
                if (Unity.Services.Core.UnityServices.State == Unity.Services.Core.ServicesInitializationState.Uninitialized)
                {
                    await Unity.Services.Core.UnityServices.InitializeAsync();
                }

                // Initialize Authentication if not already signed in
                if (!Unity.Services.Authentication.AuthenticationService.Instance.IsSignedIn)
                {
                    await Unity.Services.Authentication.AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                m_ServicesInitialized = true;
                m_StatusMessage = "Online Services Ready";
                OnStatusChanged?.Invoke(m_StatusMessage);
                return true;
            }
            catch (Exception ex)
            {
                Debug.Log($"[ConquestSessionManager] Online Services: {ex.Message}. LAN / Direct Connect available.");
                m_ServicesInitialized = false;
                m_StatusMessage = "Local / LAN Mode (Direct Connect)";
                OnStatusChanged?.Invoke(m_StatusMessage);
                return false;
            }
        }

        public void StartSoloGame(string mapSceneName, int tickets = 150, int botsPerTeam = 4, BotDifficulty difficulty = BotDifficulty.Normal)
        {
            isSoloMode = true;
            ClearPlayerAssignments();
            sessionName = "Solo Practice";
            reinforcementTickets = tickets;
            targetTeamSize = botsPerTeam;
            enableBotFilling = true;
            botDifficulty = difficulty;

            // Solo stays on loopback; set explicitly because the transport persists between sessions.
            EnsureNetworkManagerConfigured("127.0.0.1", 7777, "127.0.0.1");

            if (NetworkManager.Singleton != null)
            {
                if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
                {
                    NetworkManager.Singleton.Shutdown();
                }

                NetworkManager.Singleton.StartHost();
                ConquestSceneTransitionManager.Instance?.TransitionToMap(mapSceneName, true);
            }
        }

        public bool StartHostGame(string mapId, string mapSceneName, string sessName, int maxPlayers, int tickets, int teamSize, bool botFill, BotDifficulty diff)
        {
            isSoloMode = false;
            ClearPlayerAssignments();
            // Must be stored before StartHost(): the lobby reads these in OnNetworkSpawn.
            selectedMapId = mapId;
            selectedMapScene = mapSceneName;
            sessionName = sessName;
            maxHumanPlayers = maxPlayers;
            reinforcementTickets = tickets;
            targetTeamSize = teamSize;
            enableBotFilling = botFill;
            botDifficulty = diff;
            lastJoinCode = $"{UnityEngine.Random.Range(1000, 9999)}-CQ";

            // Listen on all interfaces so LAN players can reach the host (loopback-only refuses them).
            EnsureNetworkManagerConfigured("127.0.0.1", connectPort, AnyListenAddress);

            if (NetworkManager.Singleton != null)
            {
                if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
                {
                    NetworkManager.Singleton.Shutdown();
                }

                bool success = NetworkManager.Singleton.StartHost();
                if (success)
                {
                    m_StatusMessage = $"Hosting on {GetHostAddressSummary(connectPort)}";
                    Debug.Log($"[ConquestSessionManager] {m_StatusMessage}");
                    OnStatusChanged?.Invoke(m_StatusMessage);
                    return true;
                }
            }

            m_StatusMessage = "Failed to start Host session";
            OnStatusChanged?.Invoke(m_StatusMessage);
            return false;
        }

        public bool StartClientConnect(string address, ushort port)
        {
            isSoloMode = false;
            connectAddress = string.IsNullOrEmpty(address) ? "127.0.0.1" : address;
            connectPort = port > 0 ? port : (ushort)7777;

            EnsureNetworkManagerConfigured(connectAddress, connectPort);

            if (NetworkManager.Singleton != null)
            {
                if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
                {
                    NetworkManager.Singleton.Shutdown();
                }

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

        public void LeaveSession()
        {
            m_StatusMessage = "Leaving session...";
            OnStatusChanged?.Invoke(m_StatusMessage);
            ClearPlayerAssignments();

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
                    transport.ConnectionData.Address = address;
                    transport.ConnectionData.Port = port;
                    transport.ConnectionData.ServerListenAddress = listenAddress ?? "";
                }
            }
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
