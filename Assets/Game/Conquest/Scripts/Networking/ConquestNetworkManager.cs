using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Gameplay;
using Game.Conquest.Player;

namespace Game.Conquest.Networking
{
    public class ConquestNetworkManager : MonoBehaviour
    {
        public static ConquestNetworkManager Instance { get; private set; }

        [Header("Spawn Points")]
        [SerializeField] private List<Transform> redSpawnPoints = new List<Transform>();
        [SerializeField] private List<Transform> blueSpawnPoints = new List<Transform>();
        [SerializeField] private List<Transform> neutralSpawnPoints = new List<Transform>();

        [Header("Team Balancing")]
        private readonly List<ConquestPlayer> m_ConnectedPlayers = new List<ConquestPlayer>();
        private int m_RedCount = 0;
        private int m_BlueCount = 0;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;

                if (NetworkManager.Singleton.SceneManager != null)
                {
                    NetworkManager.Singleton.SceneManager.OnSceneEvent += HandleSceneEvent;
                }
            }

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                RegisterExistingPlayers();

                // The host's own load may finish before this Start subscribes; remote clients
                // are handled by their LoadComplete events.
                if (NetworkManager.Singleton.IsHost)
                {
                    EnsurePlayerForClient(NetworkManager.Singleton.LocalClientId);
                }
            }
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;

                if (NetworkManager.Singleton.SceneManager != null)
                {
                    NetworkManager.Singleton.SceneManager.OnSceneEvent -= HandleSceneEvent;
                }
            }
        }

        private void HandleSceneEvent(SceneEvent sceneEvent)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                // LoadComplete: a client finished loading this map. SynchronizeComplete: a late
                // joiner finished its initial sync while the match is running.
                if (sceneEvent.SceneEventType == SceneEventType.LoadComplete ||
                    sceneEvent.SceneEventType == SceneEventType.SynchronizeComplete)
                {
                    EnsurePlayerForClient(sceneEvent.ClientId);
                }
            }
        }

        /// <summary>
        /// Server: give a client its player object once it is in the map, or re-place the one it has.
        /// Players are no longer auto-spawned on connect, so nobody has a player in the lobby.
        /// </summary>
        private void EnsurePlayerForClient(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            if (!nm.ConnectedClients.TryGetValue(clientId, out var client)) return; // e.g. the dedicated server itself

            if (client.PlayerObject != null)
            {
                var existing = client.PlayerObject.GetComponent<ConquestPlayer>();
                if (existing != null && !existing.isBot) RegisterPlayer(existing);
                return;
            }

            var prefab = nm.NetworkConfig.PlayerPrefab;
            if (prefab == null)
            {
                Debug.LogError("[ConquestNetworkManager] NetworkConfig.PlayerPrefab is not set; cannot spawn players.");
                return;
            }

            // Spawned at the origin; ConquestPlayer.OnNetworkSpawn -> RegisterPlayer applies the
            // lobby identity and moves it to a team spawn point.
            var instance = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            var netObj = instance.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError("[ConquestNetworkManager] Player prefab has no NetworkObject.");
                Destroy(instance);
                return;
            }
            netObj.SpawnAsPlayerObject(clientId);
        }

        private void RegisterExistingPlayers()
        {
            var players = FindObjectsByType<ConquestPlayer>(FindObjectsInactive.Exclude);
            foreach (var p in players)
            {
                if (p != null && !p.isBot)
                {
                    RegisterPlayer(p);
                }
            }
        }

        private void HandleClientConnected(ulong clientId)
        {
            Debug.Log($"[ConquestNetworkManager] Client {clientId} connected.");
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            Debug.Log($"[ConquestNetworkManager] Client {clientId} disconnected.");
        }

        public void RegisterPlayer(ConquestPlayer player)
        {
            if (!m_ConnectedPlayers.Contains(player))
            {
                m_ConnectedPlayers.Add(player);

                // Use the lobby's choices when the player came through the lobby; otherwise
                // (solo host, late joiner) auto-balance onto the smaller team.
                Team assignedTeam;
                string assignedName = $"Player_{player.OwnerClientId}";
                int assignedClass = 0;
                var session = ConquestSessionManager.Instance;
                if (session != null && session.TryGetPlayerAssignment(player.OwnerClientId, out var assignment)
                    && (assignment.team == Team.Red || assignment.team == Team.Blue))
                {
                    assignedTeam = assignment.team;
                    assignedClass = assignment.classIndex;
                    if (!string.IsNullOrEmpty(assignment.playerName)) assignedName = assignment.playerName;
                }
                else
                {
                    assignedTeam = (m_RedCount <= m_BlueCount) ? Team.Red : Team.Blue;
                    // Late joiners never saw the lobby; use the name from their connection payload.
                    if (session != null && session.TryGetApprovedName(player.OwnerClientId, out var payloadName))
                    {
                        assignedName = payloadName;
                    }
                }

                if (assignedTeam == Team.Red) m_RedCount++;
                else m_BlueCount++;

                player.ServerApplyIdentity(assignedTeam, assignedName, assignedClass);

                // Position at spawn point
                if (GetSpawnPointForTeam(assignedTeam, out Vector3 spawnPos, out Quaternion spawnRot))
                {
                    player.SpawnAt(spawnPos, spawnRot);
                }

                Debug.Log($"[ConquestNetworkManager] Player {player.OwnerClientId} assigned to {assignedTeam}. (Red: {m_RedCount}, Blue: {m_BlueCount})");
            }
            else
            {
                // Ensure player is positioned at an active spawn point on the current map
                if (GetSpawnPointForTeam(player.playerTeam.Value, out Vector3 spawnPos, out Quaternion spawnRot))
                {
                    player.SpawnAt(spawnPos, spawnRot);
                }
            }
        }

        public void UnregisterPlayer(ConquestPlayer player)
        {
            if (m_ConnectedPlayers.Remove(player))
            {
                if (player.playerTeam.Value == Team.Red) m_RedCount = Mathf.Max(0, m_RedCount - 1);
                else if (player.playerTeam.Value == Team.Blue) m_BlueCount = Mathf.Max(0, m_BlueCount - 1);
            }
        }

        public bool GetSpawnPointForTeam(Team team, out Vector3 position, out Quaternion rotation)
        {
            List<Transform> list = (team == Team.Red) ? redSpawnPoints : (team == Team.Blue ? blueSpawnPoints : neutralSpawnPoints);
            if (list != null && list.Count > 0)
            {
                int index = Random.Range(0, list.Count);
                Transform pt = list[index];
                if (pt != null)
                {
                    position = pt.position;
                    rotation = pt.rotation;

                    // Ensure spawn position is above ground
                    if (UnityEngine.Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 15f))
                    {
                        position = hit.point + Vector3.up * 0.15f;
                    }

                    return true;
                }
            }

            // Fallback default
            position = team == Team.Red ? new Vector3(-15f, 1.5f, 0f) : new Vector3(15f, 1.5f, 0f);
            if (UnityEngine.Physics.Raycast(position + Vector3.up * 5f, Vector3.down, out RaycastHit fallbackHit, 20f))
            {
                position = fallbackHit.point + Vector3.up * 0.15f;
            }
            rotation = Quaternion.identity;
            return true;
        }

        public void OnPlayerKilled(ConquestPlayer victim, ulong killerId)
        {
            Debug.Log($"[ConquestNetworkManager] Player {victim.OwnerClientId} ({victim.playerTeam.Value}) was killed by object {killerId}");
            ConquestMatchManager.Instance?.OnPlayerDied(victim, killerId);
        }
    }
}
