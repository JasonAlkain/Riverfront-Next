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
        private float m_DeathTimestamp = 0f;
        private int m_PendingClassIndex = 0;

        public Team CurrentTeam => playerTeam.Value;
        public ConquestHealth Health => m_Health;
        public ConquestPlayerMovement Movement => m_Movement;
        public ConquestWeapon Weapon => m_Weapon;
        public float RespawnWaitTime => respawnWaitTime;
        public float RemainingRespawnTime => Mathf.Max(0f, (m_DeathTimestamp + respawnWaitTime) - Time.time);
        public string SelectedPostName => m_SelectedPostName;
        public int CurrentClassIndex => selectedClassIndex.Value;
        public IReadOnlyList<ConquestClassData> AvailableClasses => availableClasses;

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
            ConfigureLocalPerspective();
            ApplyClass(selectedClassIndex.Value);

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

            int maxIndex = availableClasses != null ? availableClasses.Count - 1 : 0;
            classIndex = Mathf.Clamp(classIndex, 0, Mathf.Max(0, maxIndex));

            playerTeam.Value = team;
            if (!string.IsNullOrEmpty(displayName)) playerName.Value = displayName;
            m_PendingClassIndex = classIndex;
            if (selectedClassIndex.Value != classIndex)
            {
                selectedClassIndex.Value = classIndex;
            }
            // Server applies directly; clients pick it up via HandleClassChanged.
            ApplyClass(classIndex);
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
            if (m_Health != null && m_Health.IsDead)
            {
                selectedClassIndex.Value = classIndex;
                ApplyClass(classIndex);
            }
            else
            {
                m_PendingClassIndex = classIndex;
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

        private void HandleDeath(ulong killerId)
        {
            m_DeathTimestamp = Time.time;
            if (thirdPersonVisualsRoot != null) thirdPersonVisualsRoot.SetActive(false);
            if (firstPersonVisualsRoot != null) firstPersonVisualsRoot.SetActive(false);

            if (IsServer)
            {
                ConquestNetworkManager.Instance?.OnPlayerKilled(this, killerId);
                StartCoroutine(ServerRespawnCoroutine());
            }
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
                if (IsOwner)
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
            m_Movement.Teleport(position, rotation);
            ConfigureLocalPerspective();
            if (IsOwner && !isBot)
            {
                m_Movement.SetCursorLock(true);
            }
        }
    }
}
