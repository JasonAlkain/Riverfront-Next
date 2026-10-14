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

                // Add host
                AddPlayer(NetworkManager.Singleton.LocalClientId, true);
            }
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
            // Balance team assignment
            int redCount = 0;
            int blueCount = 0;
            for (int i = 0; i < lobbyPlayers.Count; i++)
            {
                if (lobbyPlayers[i].team == Team.Red) redCount++;
                else if (lobbyPlayers[i].team == Team.Blue) blueCount++;
            }

            Team assignedTeam = redCount <= blueCount ? Team.Red : Team.Blue;

            LobbyPlayerState state = new LobbyPlayerState
            {
                clientId = clientId,
                playerName = isHost ? "Host" : $"Player_{clientId}",
                team = assignedTeam,
                classIndex = 0, // Assault
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

        public void StartMatch()
        {
            if (!IsServer) return;

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
