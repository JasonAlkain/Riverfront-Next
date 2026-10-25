using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Gameplay;
using Game.Conquest.Combat;
using Game.Conquest.Networking;
using Game.Conquest.Classes;
using Game.Conquest.Objectives;

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
        [Tooltip("Shared class list; a class index is a position in it.")]
        [SerializeField] private ConquestClassRoster classRoster;

        [Header("Visuals")]
        [SerializeField] private Renderer[] bodyRenderers;
        [SerializeField] private Material redTeamMaterial;
        [SerializeField] private Material blueTeamMaterial;
        [SerializeField] private GameObject thirdPersonVisualsRoot;
        [SerializeField] private GameObject firstPersonVisualsRoot;

        [Header("Death and spawn protection")]
        [Tooltip("Pooled effect where a combatant dies, seen by every client.")]
        [SerializeField] private GameObject deathEffect;
        [SerializeField] private float deathEffectSeconds = 2.5f;
        [Tooltip("Blue team bodies fall as a cosmetic ragdoll and dissolve, instead of the death effect.")]
        [SerializeField] private bool ragdollBlueTeam = true;
        [Tooltip("Red team bodies fall as a cosmetic ragdoll and dissolve, instead of the death effect.")]
        [SerializeField] private bool ragdollRedTeam = false;
        [Tooltip("Placeholder: seconds a ragdoll lies before it dissolves.")]
        [SerializeField] private float ragdollSeconds = 2.5f;
        [Tooltip("Placeholder: seconds the dissolve takes.")]
        [SerializeField] private float ragdollDissolveSeconds = 1f;
        [Tooltip("Placeholder: m/s the ragdoll is knocked away from the killer.")]
        [SerializeField] private float ragdollPush = 3f;
        [Tooltip("Placeholder: blinks per second of a spawn-protected combatant's body, as others see it.")]
        [SerializeField] private float protectionBlinkRate = 6f;
        private bool m_BlinkHidden;

        /// <summary>The next deploy is this human's first into the match, which costs no ticket.
        /// Replicated to the owner so the deploy screen knows.</summary>
        private NetworkVariable<bool> freeDeploy = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        [Header("Respawn Settings")]
        [Tooltip("Placeholder: respawn wait used only when the match has no game mode (normally the mode's Respawn Seconds applies).")]
        [SerializeField] private float respawnWaitTime = 1.0f;

        private ConquestPlayerMovement m_Movement;
        private ConquestHealth m_Health;
        private ConquestWeapon m_Weapon;
        private ConquestThrowables m_Throwables;
        private string m_SelectedPostName = "";
        private int m_PendingClassIndex = 0;

        public Team CurrentTeam => playerTeam.Value;
        public ConquestHealth Health => m_Health;
        public ConquestPlayerMovement Movement => m_Movement;
        public ConquestWeapon Weapon => m_Weapon;
        /// <summary>Grenades and other throwables.</summary>
        public ConquestThrowables Throwables => m_Throwables;
        /// <summary>Server: the wait after a death before deploying again, from the match's game mode.</summary>
        public float RespawnWaitTime =>
            ConquestMatchManager.Instance != null ? ConquestMatchManager.Instance.RespawnSeconds(respawnWaitTime) : respawnWaitTime;
        public float RemainingRespawnTime =>
            NetworkManager == null ? 0f : Mathf.Max(0f, (float)(deployAvailableAt.Value - NetworkManager.ServerTime.Time));
        public string SelectedPostName => m_SelectedPostName;
        /// <summary>Owner or server: this player's next deploy is paid for (a free first deploy, or a ticket the team can spend).</summary>
        public bool HasTicketForDeploy
        {
            get
            {
                var match = ConquestMatchManager.Instance;
                return freeDeploy.Value || match == null || match.CanAffordRespawn(playerTeam.Value);
            }
        }
        /// <summary>Class this player will deploy as (the local choice, before the server confirms it).</summary>
        public int PendingClassIndex => m_PendingClassIndex;
        public int CurrentClassIndex => selectedClassIndex.Value;
        public IReadOnlyList<ConquestClassData> AvailableClasses => classRoster != null ? classRoster.Classes : System.Array.Empty<ConquestClassData>();
        public ConquestClassRoster ClassRoster => classRoster;
        /// <summary>This player's team's faction (weapon model, bolt look); null while Neutral. Resolved every time, never cached.</summary>
        public ConquestFactionData CurrentFaction => classRoster != null ? classRoster.FactionFor(CurrentTeam) : null;
        /// <summary>The class this player is currently playing, or null if the index is out of range.</summary>
        public ConquestClassData CurrentClassData => classRoster != null ? classRoster.Get(selectedClassIndex.Value) : null;

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
            m_Throwables = GetComponent<ConquestThrowables>();
        }

        public override void OnNetworkSpawn()
        {
            playerTeam.OnValueChanged += HandleTeamChanged;
            selectedClassIndex.OnValueChanged += HandleClassChanged;
            m_Health.OnDied += HandleDeath;
            m_Health.OnRespawned += HandleRespawn;

            ApplyTeamVisuals(playerTeam.Value);
            // A late joiner can see a player who is still waiting to deploy: keep it hidden.
            if (m_Health.IsDead) HideBody();
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
            // The faction (magazine or heat) changed with the team: start the weapon fresh. Not
            // ApplyClass, which would also reset health.
            if (IsServer && m_Weapon != null) m_Weapon.ServerResetAmmo();
        }

        private void HandleClassChanged(int oldClass, int newClass)
        {
            ApplyClass(newClass);
        }

        public void ApplyClass(int classIdx)
        {
            ConquestClassData data = classRoster != null ? classRoster.Get(classIdx) : null;
            if (data != null)
            {
                if (m_Health != null) m_Health.SetMaxHealth(data.maxHealth);
                if (m_Movement != null) m_Movement.SetSpeed(data.walkSpeed, data.sprintSpeed);
                if (m_Weapon != null) m_Weapon.ApplyClass(data);
                if (m_Throwables != null && IsServer) m_Throwables.ServerRefill(data);
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
            return classRoster != null ? classRoster.ClampIndex(classIndex) : 0;
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

        /// <summary>
        /// The friendly command post this player is standing in, if any: owned by their team, not
        /// contested, and with the player inside its capture zone. Runs on any peer; the server's
        /// answer is the one that counts.
        /// </summary>
        public ConquestCommandPost FriendlyPostHere()
        {
            var match = ConquestMatchManager.Instance;
            if (match == null || match.IsMatchOver || m_Health == null || m_Health.IsDead) return null;
            Team team = CurrentTeam;
            if (team == Team.Neutral) return null;
            var posts = match.CommandPosts;
            for (int i = 0; i < posts.Count; i++)
            {
                var post = posts[i];
                if (post != null && post.CurrentOwner == team && !post.IsContested && post.IsInCaptureZone(transform.position)) return post;
            }
            return null;
        }

        /// <summary>Owner: ask to become <paramref name="classIndex"/> now, at the friendly post the player stands in.</summary>
        public void ChangeClassAtPost(int classIndex)
        {
            if (IsOwner) ChangeClassAtPostServerRpc(classIndex);
        }

        [ServerRpc]
        private void ChangeClassAtPostServerRpc(int classIndex)
        {
            // The client only asks: the server checks the post itself, so the class can't be
            // swapped away from one (or while dead, which the deploy screen handles).
            if (isBot || FriendlyPostHere() == null) return;
            classIndex = ClampClassIndex(classIndex);
            if (classIndex == selectedClassIndex.Value) return;
            // A fresh kit: ApplyClass resets health to the new class's maximum, the weapon's ammo
            // and the throwables, the same as deploying with that class.
            ServerSetClass(classIndex);
        }

        /// <summary>Server: an ammo droid's or a station's pulse. Refills both weapons and gives back one throwable.</summary>
        public void ServerResupply()
        {
            if (!IsServer || m_Health.IsDead) return;
            if (m_Weapon != null) m_Weapon.ResupplyAmmo();
            if (m_Throwables != null) m_Throwables.ServerGiveOne();
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

        // Spawn protection cue: other peers see the body blink until protection ends (by time,
        // or the moment this player fires or throws). Your own view shows a HUD label instead.
        private void Update()
        {
            if (!IsSpawned || bodyRenderers == null || m_Health == null || (IsOwner && !isBot)) return;
            bool hide = m_Health.IsSpawnProtected && Mathf.Repeat(Time.time * protectionBlinkRate, 1f) < 0.35f;
            if (hide == m_BlinkHidden) return;
            m_BlinkHidden = hide;
            foreach (var r in bodyRenderers)
            {
                if (r != null) r.enabled = !hide;
            }
        }

        // Dead or waiting to deploy: invisible and without collision.
        private void HideBody()
        {
            if (thirdPersonVisualsRoot != null) thirdPersonVisualsRoot.SetActive(false);
            if (firstPersonVisualsRoot != null) firstPersonVisualsRoot.SetActive(false);
            m_Movement.SetBodyCollisionEnabled(false);
        }

        // Knocked away from whoever made the kill (a little up), or backwards for a self-kill.
        private Vector3 RagdollVelocity(ulong killerId)
        {
            Vector3 away = -transform.forward;
            if (killerId != ConquestHealth.NoAttacker && killerId != NetworkObjectId && NetworkManager != null &&
                NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(killerId, out var killer) && killer != null)
            {
                away = transform.position - killer.transform.position;
            }
            away.y = 0f;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : -transform.forward;
            return away * ragdollPush + Vector3.up * ragdollPush * 0.3f;
        }

        private void HandleDeath(ulong killerId)
        {
            // Everyone sees a ragdoll team's body fall and dissolve a few seconds later; anyone else
            // (or a body that can't ragdoll) goes in a burst at once. A dedicated server draws nothing.
            if (IsClient)
            {
                bool ragdolls = CurrentTeam == Team.Blue ? ragdollBlueTeam : CurrentTeam == Team.Red && ragdollRedTeam;
                bool ragdolled = ragdolls && ConquestRagdoll.Spawn(thirdPersonVisualsRoot, RagdollVelocity(killerId), ragdollSeconds, ragdollDissolveSeconds);
                if (!ragdolled && deathEffect != null)
                {
                    Game.Conquest.Combat.ConquestFxPool.Play(deathEffect, transform.position + Vector3.up, Quaternion.identity, deathEffectSeconds);
                }
            }
            HideBody();
            // The chosen post is kept: the next deploy goes back there while it is still friendly
            // (the deploy screen and the server both check), so a 1 s respawn really is 1 s.

            if (IsServer)
            {
                deployAvailableAt.Value = NetworkManager.ServerTime.Time + RespawnWaitTime;
                ConquestNetworkManager.Instance?.OnPlayerKilled(this, killerId);
                // Bots respawn by themselves; humans wait on the deploy screen (DeployServerRpc).
                if (isBot) StartCoroutine(ServerRespawnCoroutine());
            }
        }

        /// <summary>
        /// Server: hold a human player out of play until they choose a class and post and deploy.
        /// Used when they first enter a match. The position only parks their camera somewhere sensible.
        /// </summary>
        /// <param name="waitRespawnDelay">True for a player joining a running match: they wait the
        /// normal respawn delay, like everyone else coming back into the fight.</param>
        public void ServerHoldForDeploy(Vector3 parkPosition, Quaternion parkRotation, bool waitRespawnDelay = false)
        {
            if (!IsServer || isBot) return;
            m_SelectedPostName = "";
            freeDeploy.Value = true;
            deployAvailableAt.Value = NetworkManager.ServerTime.Time + (waitRespawnDelay ? RespawnWaitTime : 0f);
            m_Health.ServerHold();
            HideBody();
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
            // The first deploy into a match is free; every later one costs the team a ticket.
            if (!freeDeploy.Value && !match.ServerTrySpendRespawn(this)) return;
            freeDeploy.Value = false;

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
            m_Movement.SetBodyCollisionEnabled(true);
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
            yield return new WaitForSeconds(RespawnWaitTime);

            while (true)
            {
                var match = ConquestMatchManager.Instance;
                // A decided match keeps its dead out: the results screen follows the banner.
                if (match == null || match.IsMatchOver) yield break;

                // Safely retire excess bots if new humans joined
                if (isBot && ConquestBotManager.Instance != null && ConquestBotManager.Instance.ShouldRetireBot(playerTeam.Value))
                {
                    GetComponent<NetworkObject>()?.Despawn(true);
                    yield break;
                }

                // No ticket this bot may take (out, or kept for a dead player): wait and ask again.
                if (match.ServerTrySpendRespawn(this)) break;
                yield return s_TicketRetryWait;
            }

            ServerRespawnNow();
        }

        // Placeholder: how often a bot waiting on a ticket asks again.
        private static readonly WaitForSeconds s_TicketRetryWait = new WaitForSeconds(0.5f);

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
