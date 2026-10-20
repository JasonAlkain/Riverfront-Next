using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Gameplay;
using Game.Conquest.Combat;
using Game.Conquest.Networking;
using Game.Conquest.Classes;

namespace Game.Conquest.Player
{
    [RequireComponent(typeof(ConquestPlayerMovement))]
    [RequireComponent(typeof(ConquestHealth))]
    public class ConquestPlayer : NetworkBehaviour
    {
        [Header("Team & Identity")]
        public NetworkVariable<Team> playerTeam = new NetworkVariable<Team>(
            Team.Neutral,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<FixedString64Bytes> playerName = new NetworkVariable<FixedString64Bytes>(
            "Player",
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>Server time from which this player may deploy (spawn) again.</summary>
        public NetworkVariable<double> deployAvailableAt = new NetworkVariable<double>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<int> selectedClassIndex = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public bool isBot = false;

        [Header("Classes")]
        [SerializeField] private List<ConquestClassData> availableClasses = new List<ConquestClassData>();

        [Header("Visuals")]
        [SerializeField] private Renderer[] bodyRenderers;
        [SerializeField] private Material redTeamMaterial;
        [SerializeField] private Material blueTeamMaterial;
        [SerializeField] private GameObject thirdPersonVisualsRoot;
        [SerializeField] private GameObject firstPersonVisualsRoot;

        [Header("Respawn Settings")]
        [SerializeField] private float respawnWaitTime = 5.0f;

        private ConquestPlayerMovement m_Movement;
        private ConquestHealth m_Health;
        private ConquestWeapon m_Weapon;
        private string m_SelectedPostName = "";
        private int m_PendingClassIndex = 0;

        public Team CurrentTeam => playerTeam.Value;
        public ConquestHealth Health => m_Health;
        public ConquestPlayerMovement Movement => m_Movement;
        public ConquestWeapon Weapon => m_Weapon;
        public float RespawnWaitTime => respawnWaitTime;
        public float RemainingRespawnTime =>
            NetworkManager == null ? 0f : Mathf.Max(0f, (float)(deployAvailableAt.Value - NetworkManager.ServerTime.Time));
        public string SelectedPostName => m_SelectedPostName;
        /// <summary>Class this player will deploy as (the local choice, before the server confirms it).</summary>
        public int PendingClassIndex => m_PendingClassIndex;
        public int CurrentClassIndex => selectedClassIndex.Value;
        public IReadOnlyList<ConquestClassData> AvailableClasses => availableClasses;

        /// <summary>This peer's own (non-bot) player once it has spawned; null before that and after despawn.</summary>
        public static ConquestPlayer Local { get; private set; }

        private static readonly List<ConquestPlayer> s_All = new List<ConquestPlayer>();
        /// <summary>Every spawned player and bot on this peer, alive or dead. Saves scanning the scene or physics.</summary>
        public static IReadOnlyList<ConquestPlayer> All => s_All;

        private void Awake()
        {
            m_Movement = GetComponent<ConquestPlayerMovement>();
            m_Health = GetComponent<ConquestHealth>();
            m_Weapon = GetComponent<ConquestWeapon>();
        }

        public override void OnNetworkSpawn()
        {
            playerTeam.OnValueChanged += HandleTeamChanged;
            selectedClassIndex.OnValueChanged += HandleClassChanged;
            m_Health.OnDied += HandleDeath;
            m_Health.OnRespawned += HandleRespawn;

            ApplyTeamVisuals(playerTeam.Value);
            // A late joiner can see a player who is still waiting to deploy: keep it hidden.
            if (m_Health.IsDead) HideVisuals();
            else ConfigureLocalPerspective();
            ApplyClass(selectedClassIndex.Value);
            m_PendingClassIndex = selectedClassIndex.Value;

            if (IsOwner && !isBot) Local = this;
            if (!s_All.Contains(this)) s_All.Add(this);

            if (IsServer && !isBot)
            {
                ConquestNetworkManager.Instance?.RegisterPlayer(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            playerTeam.OnValueChanged -= HandleTeamChanged;
            selectedClassIndex.OnValueChanged -= HandleClassChanged;
            m_Health.OnDied -= HandleDeath;
            m_Health.OnRespawned -= HandleRespawn;

            if (Local == this) Local = null;
            s_All.Remove(this);

            if (IsServer && !isBot)
            {
                ConquestNetworkManager.Instance?.UnregisterPlayer(this);
            }
        }

        public void SetAvailableClasses(List<ConquestClassData> classes)
        {
            availableClasses = classes;
            ApplyClass(selectedClassIndex.Value);
        }

        private void ConfigureLocalPerspective()
        {
            if (isBot)
            {
                if (thirdPersonVisualsRoot != null) thirdPersonVisualsRoot.SetActive(true);
                if (firstPersonVisualsRoot != null) firstPersonVisualsRoot.SetActive(false);
                return;
            }

            if (IsOwner)
            {
                if (thirdPersonVisualsRoot != null) thirdPersonVisualsRoot.SetActive(false);
                if (firstPersonVisualsRoot != null) firstPersonVisualsRoot.SetActive(true);
            }
            else
            {
                if (thirdPersonVisualsRoot != null) thirdPersonVisualsRoot.SetActive(true);
                if (firstPersonVisualsRoot != null) firstPersonVisualsRoot.SetActive(false);
            }
        }

        private void HandleTeamChanged(Team oldTeam, Team newTeam)
        {
            ApplyTeamVisuals(newTeam);
        }

        private void HandleClassChanged(int oldClass, int newClass)
        {
            ApplyClass(newClass);
        }

        public void ApplyClass(int classIdx)
        {
            if (availableClasses != null && classIdx >= 0 && classIdx < availableClasses.Count)
            {
                ConquestClassData data = availableClasses[classIdx];
                if (data != null)
                {
                    if (m_Health != null) m_Health.SetMaxHealth(data.maxHealth);
                    if (m_Movement != null) m_Movement.SetSpeed(data.walkSpeed, data.sprintSpeed);
                    if (m_Weapon != null) m_Weapon.ApplyClass(data);
                }
            }
        }

        /// <summary>
        /// Server-only: apply the identity chosen in the lobby (or by auto-balance).
        /// Also sets the pending class so the first respawn doesn't revert to class 0.
        /// </summary>
        public void ServerApplyIdentity(Team team, string displayName, int classIndex)
        {
            if (!IsServer) return;

            playerTeam.Value = team;
            if (!string.IsNullOrEmpty(displayName)) playerName.Value = displayName;
            ServerSetClass(classIndex);
        }

        /// <summary>
        /// Server-only: make this the player's class now and for every later respawn.
        /// The pending class is the server's single source of truth: HandleRespawn re-applies it,
        /// so anything that changes the class without updating it gets reverted on the next spawn.
        /// </summary>
        public void ServerSetClass(int classIndex)
        {
            if (!IsServer) return;

            classIndex = ClampClassIndex(classIndex);
            m_PendingClassIndex = classIndex;
            if (selectedClassIndex.Value != classIndex)
            {
                selectedClassIndex.Value = classIndex;
            }
            // Server applies directly; clients pick it up via HandleClassChanged.
            ApplyClass(classIndex);
        }

        private int ClampClassIndex(int classIndex)
        {
            int maxIndex = availableClasses != null ? availableClasses.Count - 1 : 0;
            return Mathf.Clamp(classIndex, 0, Mathf.Max(0, maxIndex));
        }

        public void SelectClass(int classIndex)
        {
            m_PendingClassIndex = classIndex;
            if (IsOwner)
            {
                SelectClassServerRpc(classIndex);
            }
        }

        [ServerRpc]
        public void SelectClassServerRpc(int classIndex)
        {
            // While dead the new class applies at once; while alive it waits for the next respawn.
            if (m_Health != null && m_Health.IsDead)
            {
                ServerSetClass(classIndex);
            }
            else
            {
                m_PendingClassIndex = ClampClassIndex(classIndex);
            }
        }

        public void ApplyTeamVisuals(Team team)
        {
            Material targetMat = team == Team.Red ? redTeamMaterial : (team == Team.Blue ? blueTeamMaterial : null);
            if (targetMat == null || bodyRenderers == null) return;

            foreach (var r in bodyRenderers)
            {
                if (r != null)
                {
                    r.material = targetMat;
                }
            }
        }

        private void HideVisuals()
        {
            if (thirdPersonVisualsRoot != null) thirdPersonVisualsRoot.SetActive(false);
            if (firstPersonVisualsRoot != null) firstPersonVisualsRoot.SetActive(false);
        }

        private void HandleDeath(ulong killerId)
        {
            HideVisuals();
            // Every deploy needs a fresh choice of post.
            m_SelectedPostName = "";

            if (IsServer)
            {
                deployAvailableAt.Value = NetworkManager.ServerTime.Time + respawnWaitTime;
                ConquestNetworkManager.Instance?.OnPlayerKilled(this, killerId);
                // Bots respawn by themselves; humans wait on the deploy screen (DeployServerRpc).
                if (isBot) StartCoroutine(ServerRespawnCoroutine());
            }
        }

        /// <summary>
        /// Server: hold a human player out of play until they choose a class and post and deploy.
        /// Used when they first enter a match. The position only parks their camera somewhere sensible.
        /// </summary>
        public void ServerHoldForDeploy(Vector3 parkPosition, Quaternion parkRotation)
        {
            if (!IsServer || isBot) return;
            m_SelectedPostName = "";
            deployAvailableAt.Value = NetworkManager.ServerTime.Time;
            m_Health.ServerHold();
            HideVisuals();
            if (IsOwner && m_Movement.HasTransformAuthority) m_Movement.Teleport(parkPosition, parkRotation);
            ParkClientRpc(parkPosition, parkRotation);
        }

        [ClientRpc]
        private void ParkClientRpc(Vector3 position, Quaternion rotation)
        {
            if (m_Movement.HasTransformAuthority && !(IsServer && IsOwner))
            {
                m_Movement.Teleport(position, rotation);
            }
        }

        /// <summary>Owner: spawn with this class at this post once the deploy wait is over.</summary>
        public void Deploy(int classIndex, string postName)
        {
            if (!IsOwner || isBot) return;
            m_PendingClassIndex = classIndex;
            m_SelectedPostName = postName ?? "";
            DeployServerRpc(classIndex, m_SelectedPostName);
        }

        [ServerRpc]
        private void DeployServerRpc(int classIndex, string postName)
        {
            // The client only asks; the server decides whether the player may spawn now.
            if (isBot || !m_Health.IsDead) return;
            var match = ConquestMatchManager.Instance;
            if (match == null || match.IsMatchOver) return;
            if (NetworkManager.ServerTime.Time < deployAvailableAt.Value) return;

            ServerSetClass(classIndex);

            // An unknown or lost post falls back to another friendly post, then the team spawn,
            // so a player can always get back into the match.
            m_SelectedPostName = postName ?? "";
            ServerRespawnNow();
        }

        private void HandleRespawn()
        {
            if (IsServer && m_PendingClassIndex != selectedClassIndex.Value)
            {
                selectedClassIndex.Value = m_PendingClassIndex;
            }
            ApplyClass(selectedClassIndex.Value);
            ConfigureLocalPerspective();
        }

        [ServerRpc]
        public void SelectSpawnPostServerRpc(string postName)
        {
            m_SelectedPostName = postName;
        }

        public void SelectSpawnPost(string postName)
        {
            m_SelectedPostName = postName;
            if (IsOwner)
            {
                SelectSpawnPostServerRpc(postName);
            }
        }

        private IEnumerator ServerRespawnCoroutine()
        {
            yield return new WaitForSeconds(respawnWaitTime);

            // Safely retire excess bots if new humans joined
            if (isBot && ConquestBotManager.Instance != null && ConquestBotManager.Instance.ShouldRetireBot(playerTeam.Value))
            {
                GetComponent<NetworkObject>()?.Despawn(true);
                yield break;
            }

            ServerRespawnNow();
        }

        private void ServerRespawnNow()
        {
            Vector3 spawnPos = Vector3.zero;
            Quaternion spawnRot = Quaternion.identity;
            bool foundPost = false;

            if (ConquestMatchManager.Instance != null)
            {
                foundPost = ConquestMatchManager.Instance.GetFriendlyPostSpawnPoint(playerTeam.Value, m_SelectedPostName, out spawnPos, out spawnRot);
            }

            if (!foundPost && ConquestNetworkManager.Instance != null)
            {
                ConquestNetworkManager.Instance.GetSpawnPointForTeam(playerTeam.Value, out spawnPos, out spawnRot);
            }

            SpawnAt(spawnPos, spawnRot);
            m_Health.ResetHealth();
        }

        public void SpawnAt(Vector3 position, Quaternion rotation)
        {
            if (IsServer)
            {
                SpawnClientRpc(position, rotation);
                // Server-owned objects (bots, the host's player) are moved here; remote players
                // move themselves when the ClientRpc reaches their owner.
                if (IsOwner && m_Movement.HasTransformAuthority)
                {
                    m_Movement.Teleport(position, rotation);
                    ConfigureLocalPerspective();
                    if (!isBot)
                    {
                        m_Movement.SetCursorLock(true);
                    }
                }
            }
        }

        [ClientRpc]
        private void SpawnClientRpc(Vector3 position, Quaternion rotation)
        {
            // Only the transform authority writes the position; everyone else receives the
            // teleport through the NetworkTransform instead of fighting it locally.
            if (m_Movement.HasTransformAuthority && !(IsServer && IsOwner))
            {
                m_Movement.Teleport(position, rotation);
            }
            ConfigureLocalPerspective();
            if (IsOwner && !isBot)
            {
                m_Movement.SetCursorLock(true);
            }
        }
    }
}
