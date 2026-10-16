using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Gameplay;
using Game.Conquest.Maps;
using Game.Conquest.Networking;
using Game.Conquest.Scenes;

namespace Game.Conquest.Lobby
{
    public struct LobbyPlayerState : INetworkSerializable, IEquatable<LobbyPlayerState>
    {
        public ulong clientId;
        public FixedString64Bytes playerName;
        public Team team;
        public int classIndex;
        public bool isReady;
        public bool isHost;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref playerName);
            serializer.SerializeValue(ref team);
            serializer.SerializeValue(ref classIndex);
            serializer.SerializeValue(ref isReady);
            serializer.SerializeValue(ref isHost);
        }

        public bool Equals(LobbyPlayerState other)
        {
            return clientId == other.clientId &&
                   playerName.Equals(other.playerName) &&
                   team == other.team &&
                   classIndex == other.classIndex &&
                   isReady == other.isReady &&
                   isHost == other.isHost;
        }
    }

    public class ConquestLobbyManager : NetworkBehaviour
    {
        public static ConquestLobbyManager Instance { get; private set; }

        [Header("Registry Reference")]
        [SerializeField] private ConquestMapRegistry mapRegistry;

        // Networked Lobby State
        public NetworkVariable<FixedString64Bytes> sessionName = new NetworkVariable<FixedString64Bytes>(
            "Conquest Battle",
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<FixedString64Bytes> selectedMapScene = new NetworkVariable<FixedString64Bytes>(
            "ConquestLargeBattlefield",
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<int> reinforcementTickets = new NetworkVariable<int>(
            250,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<int> targetTeamSize = new NetworkVariable<int>(
            8,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<BotDifficulty> botDifficulty = new NetworkVariable<BotDifficulty>(
            BotDifficulty.Normal,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> enableBotFilling = new NetworkVariable<bool>(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkList<LobbyPlayerState> lobbyPlayers;

        /// <summary>Dedicated server only: seconds until the match auto-starts, or -1 when not counting down.</summary>
        public NetworkVariable<int> autoStartCountdown = new NetworkVariable<int>(
            -1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private float m_AutoStartAt = -1f;
        private bool m_MatchStarting = false;

        public event Action OnLobbyStateChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            lobbyPlayers = new NetworkList<LobbyPlayerState>();
        }

        public override void OnNetworkSpawn()
        {
            lobbyPlayers.OnListChanged += HandleListChanged;
            sessionName.OnValueChanged += (oldVal, newVal) => OnLobbyStateChanged?.Invoke();
            selectedMapScene.OnValueChanged += (oldVal, newVal) => OnLobbyStateChanged?.Invoke();
            reinforcementTickets.OnValueChanged += (oldVal, newVal) => OnLobbyStateChanged?.Invoke();
            targetTeamSize.OnValueChanged += (oldVal, newVal) => OnLobbyStateChanged?.Invoke();
            botDifficulty.OnValueChanged += (oldVal, newVal) => OnLobbyStateChanged?.Invoke();
            autoStartCountdown.OnValueChanged += (oldVal, newVal) => OnLobbyStateChanged?.Invoke();

            if (IsServer)
            {
                if (ConquestSessionManager.Instance != null)
                {
                    sessionName.Value = ConquestSessionManager.Instance.sessionName;
                    reinforcementTickets.Value = ConquestSessionManager.Instance.reinforcementTickets;
                    targetTeamSize.Value = ConquestSessionManager.Instance.targetTeamSize;
                    enableBotFilling.Value = ConquestSessionManager.Instance.enableBotFilling;
                    botDifficulty.Value = ConquestSessionManager.Instance.botDifficulty;

                    // Prefer the registry entry for the chosen id; fall back to the scene name the host picked.
                    var session = ConquestSessionManager.Instance;
                    var map = mapRegistry != null ? mapRegistry.GetMapById(session.selectedMapId) : null;
                    if (map != null && map.mapId.Equals(session.selectedMapId, StringComparison.OrdinalIgnoreCase))
                        selectedMapScene.Value = map.sceneName;
                    else if (!string.IsNullOrEmpty(session.selectedMapScene))
                        selectedMapScene.Value = session.selectedMapScene;
                    else if (map != null)
                        selectedMapScene.Value = map.sceneName;
                }

                NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;

                // A listen server's own player is in the lobby; a dedicated server has no player.
                if (IsHost)
                {
                    AddPlayer(NetworkManager.Singleton.LocalClientId, true);
                }

                // Coming back from a match: everyone is still connected, so no connect callbacks
                // will fire. Put them all back in the lobby (with their previous team, class and name).
                foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
                {
                    if (clientId == NetworkManager.ServerClientId && !IsHost) continue; // dedicated server
                    AddPlayer(clientId, IsHost && clientId == NetworkManager.Singleton.LocalClientId);
                }
            }

            // The menu opens on its title screen; when the lobby spawns (joining, or returning
            // from a match) show the lobby instead. Wait a frame so the menu's Start has run.
            StartCoroutine(ShowLobbyScreenNextFrame());
        }

        private System.Collections.IEnumerator ShowLobbyScreenNextFrame()
        {
            yield return null;
            Game.Conquest.UI.ConquestMainMenuController.Instance?.SwitchScreen(Game.Conquest.UI.MenuState.Lobby);
        }

        public override void OnNetworkDespawn()
        {
            lobbyPlayers.OnListChanged -= HandleListChanged;

            if (IsServer && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }

        private void HandleListChanged(NetworkListEvent<LobbyPlayerState> changeEvent)
        {
            OnLobbyStateChanged?.Invoke();
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (IsServer)
            {
                AddPlayer(clientId, false);
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (IsServer)
            {
                for (int i = 0; i < lobbyPlayers.Count; i++)
                {
                    if (lobbyPlayers[i].clientId == clientId)
                    {
                        lobbyPlayers.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private void AddPlayer(ulong clientId, bool isHost)
        {
            // OnClientConnectedCallback also fires for the host's own client right after
            // OnNetworkSpawn has added it, which produced a second "Player_0" card.
            for (int i = 0; i < lobbyPlayers.Count; i++)
            {
                if (lobbyPlayers[i].clientId == clientId) return;
            }

            // Balance team assignment
            int redCount = 0;
            int blueCount = 0;
            for (int i = 0; i < lobbyPlayers.Count; i++)
            {
                if (lobbyPlayers[i].team == Team.Red) redCount++;
                else if (lobbyPlayers[i].team == Team.Blue) blueCount++;
            }

            Team assignedTeam = redCount <= blueCount ? Team.Red : Team.Blue;
            int classIndex = 0; // Assault

            // Name from the connection payload (already sanitised during approval), if any.
            string name = isHost ? "Host" : $"Player_{clientId}";
            var session = ConquestSessionManager.Instance;
            if (session != null && session.TryGetApprovedName(clientId, out var approvedName))
            {
                name = approvedName;
            }

            // Back from a match: keep the team, class and name the player had (ready resets).
            if (session != null && session.TryGetPlayerAssignment(clientId, out var previous)
                && (previous.team == Team.Red || previous.team == Team.Blue))
            {
                assignedTeam = previous.team;
                classIndex = previous.classIndex;
                if (!string.IsNullOrEmpty(previous.playerName)) name = previous.playerName;
            }

            LobbyPlayerState state = new LobbyPlayerState
            {
                clientId = clientId,
                playerName = name,
                team = assignedTeam,
                classIndex = classIndex,
                isReady = isHost, // Host is ready by default
                isHost = isHost
            };

            lobbyPlayers.Add(state);
        }

        // Client controls
        [Rpc(SendTo.Server)]
        public void ToggleReadyServerRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            for (int i = 0; i < lobbyPlayers.Count; i++)
            {
                if (lobbyPlayers[i].clientId == sender)
                {
                    var p = lobbyPlayers[i];
                    p.isReady = !p.isReady;
                    lobbyPlayers[i] = p;
                    break;
                }
            }
        }

        [Rpc(SendTo.Server)]
        public void SwitchTeamServerRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            for (int i = 0; i < lobbyPlayers.Count; i++)
            {
                if (lobbyPlayers[i].clientId == sender)
                {
                    var p = lobbyPlayers[i];
                    p.team = p.team == Team.Red ? Team.Blue : Team.Red;
                    lobbyPlayers[i] = p;
                    break;
                }
            }
        }

        [Rpc(SendTo.Server)]
        public void SelectClassServerRpc(int classIdx, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            for (int i = 0; i < lobbyPlayers.Count; i++)
            {
                if (lobbyPlayers[i].clientId == sender)
                {
                    var p = lobbyPlayers[i];
                    p.classIndex = classIdx;
                    lobbyPlayers[i] = p;
                    break;
                }
            }
        }

        [Rpc(SendTo.Server)]
        public void SetNameServerRpc(string requestedName, RpcParams rpcParams = default)
        {
            string clean = ConquestSessionManager.SanitizePlayerName(requestedName);
            if (string.IsNullOrEmpty(clean)) return;

            ulong sender = rpcParams.Receive.SenderClientId;
            for (int i = 0; i < lobbyPlayers.Count; i++)
            {
                if (lobbyPlayers[i].clientId == sender)
                {
                    var p = lobbyPlayers[i];
                    p.playerName = clean;
                    lobbyPlayers[i] = p;
                    break;
                }
            }
        }

        // Host controls
        [Rpc(SendTo.Server)]
        public void SetMapSceneServerRpc(string sceneName, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != NetworkManager.Singleton.LocalClientId) return;
            selectedMapScene.Value = sceneName;
        }

        [Rpc(SendTo.Server)]
        public void SetTicketsServerRpc(int tickets, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != NetworkManager.Singleton.LocalClientId) return;
            reinforcementTickets.Value = tickets;
        }

        [Rpc(SendTo.Server)]
        public void SetTeamSizeServerRpc(int teamSize, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != NetworkManager.Singleton.LocalClientId) return;
            targetTeamSize.Value = teamSize;
        }

        [Rpc(SendTo.Server)]
        public void SetDifficultyServerRpc(BotDifficulty diff, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != NetworkManager.Singleton.LocalClientId) return;
            botDifficulty.Value = diff;
        }

        // Dedicated servers have no host to press Start: once enough humans are in and all are
        // ready, count down and start. Anyone un-readying (or leaving) cancels the countdown.
        private void Update()
        {
            if (!IsSpawned || !IsServer || m_MatchStarting) return;
            var session = ConquestSessionManager.Instance;
            if (session == null || !session.isDedicatedServer) return;

            bool canStart = lobbyPlayers.Count >= Mathf.Max(1, session.minPlayersToStart) && AllPlayersReady();
            if (!canStart)
            {
                if (m_AutoStartAt >= 0f)
                {
                    m_AutoStartAt = -1f;
                    autoStartCountdown.Value = -1;
                }
                return;
            }

            if (m_AutoStartAt < 0f) m_AutoStartAt = Time.unscaledTime + Mathf.Max(0f, session.autoStartCountdownSeconds);

            int remaining = Mathf.CeilToInt(Mathf.Max(0f, m_AutoStartAt - Time.unscaledTime));
            if (autoStartCountdown.Value != remaining) autoStartCountdown.Value = remaining;

            if (Time.unscaledTime >= m_AutoStartAt)
            {
                StartMatch();
            }
        }

        private bool AllPlayersReady()
        {
            for (int i = 0; i < lobbyPlayers.Count; i++)
            {
                if (!lobbyPlayers[i].isReady) return false;
            }
            return true;
        }

        public void StartMatch()
        {
            if (!IsServer || m_MatchStarting) return;
            m_MatchStarting = true;
            m_AutoStartAt = -1f;

            // Commit settings to SessionManager
            if (ConquestSessionManager.Instance != null)
            {
                ConquestSessionManager.Instance.reinforcementTickets = reinforcementTickets.Value;
                ConquestSessionManager.Instance.targetTeamSize = targetTeamSize.Value;
                ConquestSessionManager.Instance.enableBotFilling = enableBotFilling.Value;
                ConquestSessionManager.Instance.botDifficulty = botDifficulty.Value;

                // Persist each player's lobby choices; ConquestNetworkManager applies them when the
                // player registers in the map scene (this lobby object is destroyed by the load).
                ConquestSessionManager.Instance.ClearPlayerAssignments();
                for (int i = 0; i < lobbyPlayers.Count; i++)
                {
                    var p = lobbyPlayers[i];
                    ConquestSessionManager.Instance.SetPlayerAssignment(p.clientId, new ConquestSessionManager.PlayerAssignment
                    {
                        team = p.team,
                        classIndex = p.classIndex,
                        playerName = p.playerName.ToString()
                    });
                }
            }

            string mapScene = selectedMapScene.Value.ToString();
            ConquestSceneTransitionManager.Instance?.TransitionToMap(mapScene, true);
        }
    }
}
