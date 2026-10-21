using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using Game.Conquest.Player;
using Game.Conquest.Combat;
using Game.Conquest.Objectives;
using Game.Conquest.Classes;

namespace Game.Conquest.Gameplay
{
    public enum BotState
    {
        Idle,
        MoveToObjective,
        Capture,
        Defend,
        EngageEnemy,
        Dead,
        Retreat
    }

    public enum BotDifficulty
    {
        Easy,
        Normal,
        Hard
    }

    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(ConquestPlayer))]
    [RequireComponent(typeof(ConquestHealth))]
    [RequireComponent(typeof(ConquestWeapon))]
    public class ConquestBotController : NetworkBehaviour
    {
        [Header("State")]
        [SerializeField] private BotState currentState = BotState.Idle;
        [SerializeField] private BotDifficulty difficulty = BotDifficulty.Easy;

        [Header("Sensory & Combat")]
        [SerializeField] private float detectionRange = 35.0f;
        [SerializeField] private float fieldOfView = 120.0f;
        [SerializeField] private float aimAccuracy = 0.6f; // 0 (wild) to 1 (perfect)
        [SerializeField] private float reactionDelay = 0.5f;
        [Tooltip("Placeholder: aim error in degrees at aimAccuracy 0. About the old 2.2 m wobble at 18 m.")]
        [SerializeField] private float worstAimErrorDegrees = 7.0f;
        [Tooltip("Placeholder: aim error in degrees at aimAccuracy 1. About the old 0.2 m wobble at 18 m.")]
        [SerializeField] private float bestAimErrorDegrees = 0.6f;
        [SerializeField] private LayerMask visionMask = ConquestPhysics.ShotMask;
        [Tooltip("Seconds between perception updates. Set by difficulty.")]
        [SerializeField] private float perceptionInterval = 0.25f;
        [SerializeField] private ConquestBotPerception perception = new ConquestBotPerception();

        [Header("Combat Movement")]
        [Tooltip("How each class fights. Looked up by the bot's current class.")]
        [SerializeField] private ConquestBotClassRole[] classRoles = ConquestBotClassRole.Defaults;
        [Tooltip("Placeholder: share of the class sprint speed bots travel at (players sprint between posts).")]
        [SerializeField] private float travelSpeedShare = 0.6f;
        [Tooltip("Placeholder: share of the class walk speed bots strafe at while shooting.")]
        [SerializeField] private float strafeSpeedShare = 0.6f;
        [Tooltip("Placeholder: average seconds between side-steps while fighting.")]
        [SerializeField] private float strafeIntervalSeconds = 1.5f;
        [Tooltip("Placeholder: metres of the longest side-step.")]
        [SerializeField] private float strafeDistance = 3.5f;
        [Tooltip("Placeholder: metres around the bot searched for cover while it reloads.")]
        [SerializeField] private float coverSearchRadius = 6f;
        [Tooltip("Placeholder: share of max health below which a bot falls back to a friendly station.")]
        [Range(0f, 1f)]
        [SerializeField] private float retreatHealthShare = 0.35f;
        [Tooltip("Placeholder: share of max health at which a retreating bot returns to the fight.")]
        [Range(0f, 1f)]
        [SerializeField] private float retreatUntilHealthShare = 0.8f;
        [Tooltip("Placeholder: metres a bot will go to reach a friendly station.")]
        [SerializeField] private float retreatSearchRange = 40f;

        [Header("Defending")]
        [Tooltip("Placeholder: average seconds a defender stands at one guard spot before moving to another in the zone.")]
        [SerializeField] private float guardRepositionSeconds = 6f;

        [Header("Behavior Intervals")]
        [SerializeField] private float decisionInterval = 1.0f;
        [Tooltip("Seconds of too little progress, while the bot is trying to move, before it counts as stuck.")]
        [SerializeField] private float stuckThresholdTime = 2.5f;
        [Tooltip("Placeholder: how often the bot's position is sampled to measure progress.")]
        [SerializeField] private float stuckSampleInterval = 0.5f;
        [Tooltip("Placeholder: metres a moving bot must cover per sample to count as making progress.")]
        [SerializeField] private float stuckMinProgress = 0.5f;
        [Tooltip("Placeholder: seconds a stuck bot spends on its sidestep before resuming its route.")]
        [SerializeField] private float stuckRecoverySeconds = 1.2f;

        private NavMeshAgent m_Agent;
        private ConquestPlayer m_Player;
        private ConquestHealth m_Health;
        private ConquestWeapon m_Weapon;
        private ConquestPlayerAnimator m_Animator;

        private ConquestCommandPost m_TargetPost;
        private bool m_IsDefender;
        private float m_NextGuardMoveTime;
        private float m_NextDecisionTime;
        private float m_NextPerceptionTime;
        private Vector3 m_LastStuckSample;
        private float m_NextStuckSampleTime;
        private float m_StuckTimer;
        private float m_RecoverUntil;
        private float m_NextStationAttemptTime;
        private const float StationAttemptInterval = 3f;
        private float m_NextStrafeTime;
        private bool m_CoverSoughtThisReload;
        private ConquestSupportStation m_RetreatStation;
        private bool m_FacingManually;
        private const int CoverProbeCount = 6;
        // Map geometry is all on the Default layer; players and stations don't count as cover.
        private const int CoverMask = 1 << 0;

        public BotState CurrentState => currentState;
        public BotDifficulty Difficulty => difficulty;
        public ConquestCommandPost TargetPost => m_TargetPost;

        private void Awake()
        {
            m_Agent = GetComponent<NavMeshAgent>();
            m_Player = GetComponent<ConquestPlayer>();
            m_Health = GetComponent<ConquestHealth>();
            m_Weapon = GetComponent<ConquestWeapon>();
            m_Animator = GetComponent<ConquestPlayerAnimator>();

            m_Player.isBot = true;
            if (m_Player.Movement != null)
            {
                m_Player.Movement.isBot = true;
            }

            // Only the server steers bots. The prefab ships the agent enabled, and on a client it
            // would write the transform (and fight NetworkTransform) before OnNetworkSpawn runs.
            var network = NetworkManager.Singleton;
            if (network != null && network.IsListening && !network.IsServer)
            {
                m_Agent.enabled = false;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (m_Health != null)
            {
                m_Health.OnDied += HandleDeath;
                m_Health.OnRespawned += HandleRespawn;
            }

            ApplyDifficultySettings();

            if (IsServer)
            {
                m_Health.OnServerDamaged += HandleServerDamaged;
                currentState = BotState.Idle;
                m_NextDecisionTime = Time.time + Random.Range(0.2f, 1.0f);
                m_NextPerceptionTime = Time.time + Random.Range(0f, perceptionInterval);
                ResetStuckSampling();
            }
            else
            {
                // Clients don't run AI agent simulation; server syncs transform
                m_Agent.enabled = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (m_Health != null)
            {
                m_Health.OnDied -= HandleDeath;
                m_Health.OnRespawned -= HandleRespawn;
                m_Health.OnServerDamaged -= HandleServerDamaged;
            }
        }

        public void SetDifficulty(BotDifficulty diff)
        {
            difficulty = diff;
            ApplyDifficultySettings();
        }

        private void ApplyDifficultySettings()
        {
            switch (difficulty)
            {
                case BotDifficulty.Easy:
                    detectionRange = 32.0f;
                    fieldOfView = 110.0f;
                    aimAccuracy = 0.55f;
                    reactionDelay = 0.65f;
                    perceptionInterval = 0.35f; // Placeholder
                    break;
                case BotDifficulty.Normal:
                    detectionRange = 45.0f;
                    fieldOfView = 135.0f;
                    aimAccuracy = 0.75f;
                    reactionDelay = 0.35f;
                    perceptionInterval = 0.25f; // Placeholder
                    break;
                case BotDifficulty.Hard:
                    detectionRange = 60.0f;
                    fieldOfView = 160.0f;
                    aimAccuracy = 0.90f;
                    reactionDelay = 0.18f;
                    perceptionInterval = 0.2f; // Placeholder
                    break;
            }
        }

        private void Update()
        {
            if (!IsServer || m_Health.IsDead) return;

            // Stop combat when match has concluded
            if (ConquestMatchManager.Instance != null && ConquestMatchManager.Instance.IsMatchOver)
            {
                if (m_Agent.isActiveAndEnabled && m_Agent.isOnNavMesh)
                {
                    m_Agent.isStopped = true;
                }
                currentState = BotState.Idle;
                return;
            }

            m_FacingManually = false;
            if (m_Weapon != null && !m_Weapon.IsReloading) m_CoverSoughtThisReload = false;

            TickPerception();

            // Periodic tactical evaluation
            if (Time.time >= m_NextDecisionTime)
            {
                m_NextDecisionTime = Time.time + decisionInterval;
                EvaluateBattlefield();
            }

            // State execution
            switch (currentState)
            {
                case BotState.MoveToObjective:
                    UpdateMoveToObjective();
                    break;
                case BotState.Capture:
                    UpdateCapture();
                    break;
                case BotState.Defend:
                    UpdateDefend();
                    break;
                case BotState.EngageEnemy:
                    UpdateEngageEnemy();
                    break;
                case BotState.Retreat:
                    UpdateRetreat();
                    break;
            }

            // The agent turns the bot toward where it walks, except while the bot faces a target itself.
            if (m_Agent.isActiveAndEnabled) m_Agent.updateRotation = !m_FacingManually;

            CheckStuck();
        }

        private void EvaluateBattlefield()
        {
            // A hurt bot keeps heading for its station until healed.
            if (currentState == BotState.Retreat) return;

            // 1. A known enemy comes first (perception updates on its own, faster tick).
            if (perception.HasTarget)
            {
                currentState = BotState.EngageEnemy;
                return;
            }

            // 2. Back to the post the bot manager assigned.
            if (m_TargetPost == null)
            {
                currentState = BotState.Idle;
                ConquestBotManager.Instance?.RequestReassignment();
                return;
            }

            if (IsPostSecure(m_TargetPost))
            {
                // An attacker whose post is taken holds it until the manager sends it on.
                if (!m_IsDefender) ConquestBotManager.Instance?.RequestReassignment();
                EnterDefend();
            }
            else if (m_TargetPost.IsInCaptureZone(transform.position))
            {
                currentState = BotState.Capture;
            }
            else
            {
                currentState = BotState.MoveToObjective;
                SetAgentSpeed(TravelSpeed);
                MoveToTarget(m_TargetPost.GetCaptureTargetPosition(transform.position));
            }
        }

        /// <summary>Server: the bot manager's order. A defender holds the post; an attacker takes it.</summary>
        public void AssignObjective(ConquestCommandPost post, bool defend)
        {
            if (post == m_TargetPost && defend == m_IsDefender) return;
            m_TargetPost = post;
            m_IsDefender = defend;
            m_NextGuardMoveTime = 0f;

            // A fight carries on; anything else re-plans straight away.
            if (currentState != BotState.EngageEnemy) m_NextDecisionTime = Time.time;
        }

        /// <summary>Alive and spawned, so the bot manager may give it a post.</summary>
        public bool IsAvailableForOrders => IsSpawned && currentState != BotState.Dead && !m_Health.IsDead;
        public Team Team => m_Player.CurrentTeam;
        public bool IsDefender => m_IsDefender;
        /// <summary>Metres a defending slot counts as closer for this bot: above zero for classes suited to holding ground.</summary>
        public float DefendPreference => CurrentRole.defendPreference;

        // Owned by this team, fully captured, and no enemy standing on it.
        private bool IsPostSecure(ConquestCommandPost post)
        {
            return post.CurrentOwner == m_Player.CurrentTeam && !post.IsSpawnBlocked && post.CaptureProgress >= 0.99f;
        }

        private void EnterDefend()
        {
            if (currentState != BotState.Defend) m_NextGuardMoveTime = 0f;
            currentState = BotState.Defend;
        }

        private void MoveToTarget(Vector3 destination)
        {
            // Let a stuck bot finish its sidestep before steering it back into the obstacle.
            if (Time.time < m_RecoverUntil) return;
            if (m_Agent.isActiveAndEnabled && m_Agent.isOnNavMesh)
            {
                m_Agent.isStopped = false;
                m_Agent.SetDestination(destination);
            }
        }

        private void UpdateMoveToObjective()
        {
            if (m_TargetPost == null)
            {
                currentState = BotState.Idle;
                return;
            }

            if (m_TargetPost.IsInCaptureZone(transform.position))
            {
                if (IsPostSecure(m_TargetPost)) EnterDefend();
                else currentState = BotState.Capture;
            }
        }

        private void UpdateCapture()
        {
            if (m_TargetPost == null)
            {
                currentState = BotState.Idle;
                return;
            }

            // Remain in capture zone but at safe standoff position from center pillar
            float dist = Vector3.Distance(transform.position, m_TargetPost.transform.position);
            if (!m_TargetPost.IsInCaptureZone(transform.position) || dist > m_TargetPost.CaptureRadius * 0.9f || dist < 2.0f)
            {
                SetAgentSpeed(TravelSpeed);
                MoveToTarget(m_TargetPost.GetCaptureTargetPosition(transform.position));
            }
            else
            {
                TryDeployStationAtPost();
            }

            // Taken: hold it, and let an attacker be sent on to the next post.
            if (IsPostSecure(m_TargetPost))
            {
                if (!m_IsDefender) ConquestBotManager.Instance?.RequestReassignment();
                EnterDefend();
            }
        }

        // Engineer bots drop a station while holding a post, unless their own already covers them.
        // The weapon applies the same cooldown and team cap as for players.
        private void TryDeployStationAtPost()
        {
            if (m_Weapon == null || !m_Weapon.HasSupportStation || Time.time < m_NextStationAttemptTime) return;
            m_NextStationAttemptTime = Time.time + StationAttemptInterval;
            if (m_Weapon.ServerOwnStationCovers(transform.position)) return;
            m_Weapon.ServerTryDeployStation();
        }

        // Hold the post: stand at a guard spot inside the zone, move to a new one now and then,
        // and face the nearest enemy approaching the post so a threat is seen early.
        private void UpdateDefend()
        {
            if (m_TargetPost == null)
            {
                currentState = BotState.Idle;
                return;
            }

            // Enemies on the post, or it was lost: get into the zone and contest it.
            if (!IsPostSecure(m_TargetPost))
            {
                currentState = BotState.Capture;
                return;
            }

            if (Time.time >= m_NextGuardMoveTime)
            {
                m_NextGuardMoveTime = Time.time + Random.Range(guardRepositionSeconds * 0.75f, guardRepositionSeconds * 1.25f);
                Vector2 offset = Random.insideUnitCircle * (m_TargetPost.CaptureRadius * 0.8f);
                Vector3 guardSpot = m_TargetPost.transform.position + new Vector3(offset.x, 0f, offset.y);
                if (NavMesh.SamplePosition(guardSpot, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                {
                    SetAgentSpeed(WalkSpeed);
                    MoveToTarget(hit.position);
                }
            }

            TryDeployStationAtPost();

            if (!WantsToMove() && TryFindThreatDirection(out Vector3 threatDir))
            {
                FaceDirection(threatDir);
            }
        }

        // Direction from the bot to the living enemy nearest the post, within the planner's threat radius.
        private bool TryFindThreatDirection(out Vector3 direction)
        {
            direction = Vector3.zero;
            float threatRadius = ConquestBotManager.Instance != null ? ConquestBotManager.Instance.ObjectivePlanner.ThreatRadius : 30f;
            float bestSqr = threatRadius * threatRadius;
            Vector3 postPos = m_TargetPost.transform.position;
            var all = ConquestPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                ConquestPlayer p = all[i];
                if (p == null || p.Health.IsDead || p.CurrentTeam == m_Player.CurrentTeam || p.CurrentTeam == Team.Neutral) continue;
                float sqr = (p.transform.position - postPos).sqrMagnitude;
                if (sqr > bestSqr) continue;
                bestSqr = sqr;
                direction = p.transform.position - transform.position;
            }
            direction.y = 0f;
            return direction.sqrMagnitude > 0.01f;
        }

        private void FaceDirection(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) return;
            m_FacingManually = true;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 180.0f * Time.deltaTime);
        }

        // Fight what perception has: shoot it while it's in sight, otherwise go to where it was
        // last seen. The class role decides the range, and whether the bot closes in or holds.
        private void UpdateEngageEnemy()
        {
            if (!perception.HasTarget)
            {
                currentState = BotState.Idle;
                m_NextDecisionTime = Time.time;
                return;
            }

            if (TryStartRetreat()) return;

            ConquestBotClassRole role = CurrentRole;
            Vector3 targetPos = perception.Enemy != null && perception.TargetVisible
                ? perception.Enemy.transform.position
                : perception.LastKnownPosition;
            Vector3 toTarget = targetPos - transform.position;
            FaceDirection(toTarget);

            if (!perception.TargetVisible)
            {
                // Heavies keep firing where the target vanished, to pin it down.
                if (perception.SecondsSinceSeen <= role.suppressSeconds) FireAt(perception.AimPoint);
                SetAgentSpeed(TravelSpeed);
                MoveToTarget(perception.LastKnownPosition);
                return;
            }

            float distance = toTarget.magnitude;
            if (m_Weapon != null && m_Weapon.IsReloading)
            {
                SeekCover(targetPos);
            }
            else if (distance > role.preferredRange && role.advancesOnTarget)
            {
                SetAgentSpeed(TravelSpeed);
                MoveToTarget(targetPos);
            }
            else if (role.advancesOnTarget)
            {
                Strafe(toTarget);
            }
            else
            {
                StopAgent();
            }

            if (Time.time - perception.TargetAcquiredTime < reactionDelay) return;
            FireAt(perception.AimPoint);
        }

        // Aim error is an angle, so a bot misses by more at long range, like a person would.
        private void FireAt(Vector3 aimPoint)
        {
            if (m_Weapon == null) return;
            float aimErrorDegrees = Mathf.Lerp(worstAimErrorDegrees, bestAimErrorDegrees, aimAccuracy);
            m_Weapon.FireFromAI(aimPoint, aimErrorDegrees);
        }

        // Side-step across the line of fire every so often instead of standing still.
        private void Strafe(Vector3 toTarget)
        {
            if (Time.time < m_NextStrafeTime) return;
            m_NextStrafeTime = Time.time + Random.Range(strafeIntervalSeconds * 0.6f, strafeIntervalSeconds * 1.4f);

            Vector3 side = Vector3.Cross(Vector3.up, toTarget.normalized);
            if (Random.value < 0.5f) side = -side;
            Vector3 probe = transform.position + side * Random.Range(strafeDistance * 0.5f, strafeDistance);
            if (NavMesh.SamplePosition(probe, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                SetAgentSpeed(WalkSpeed * strafeSpeedShare);
                MoveToTarget(hit.position);
            }
        }

        // While reloading, move to a nearby spot the target can't see, if there is one.
        // A handful of line checks once per reload, not a search of the level.
        private void SeekCover(Vector3 targetPos)
        {
            if (m_CoverSoughtThisReload) return;
            m_CoverSoughtThisReload = true;

            Vector3 targetEye = targetPos + Vector3.up * 1.6f;
            for (int i = 0; i < CoverProbeCount; i++)
            {
                float angle = (i + Random.value) * (360f / CoverProbeCount);
                Vector3 probe = transform.position + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * coverSearchRadius;
                if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, 2f, NavMesh.AllAreas)) continue;
                if (!Physics.Linecast(hit.position + Vector3.up * 1.2f, targetEye, CoverMask)) continue;

                SetAgentSpeed(TravelSpeed);
                MoveToTarget(hit.position);
                return;
            }
        }

        // Low on health: an Engineer drops a station where it stands; anyone else falls back to a
        // friendly station nearby. Returns true if the bot is now retreating.
        private bool TryStartRetreat()
        {
            if (m_Health.CurrentHealth > m_Health.MaxHealth * retreatHealthShare) return false;

            if (m_Weapon != null && m_Weapon.HasSupportStation && !m_Weapon.ServerOwnStationCovers(transform.position))
            {
                m_Weapon.ServerTryDeployStation();
            }

            m_RetreatStation = FindFriendlyStation();
            if (m_RetreatStation == null) return false;
            if ((m_RetreatStation.transform.position - transform.position).sqrMagnitude <= m_RetreatStation.Radius * m_RetreatStation.Radius * 0.5f) return false;

            currentState = BotState.Retreat;
            return true;
        }

        private void UpdateRetreat()
        {
            bool healed = m_Health.CurrentHealth >= m_Health.MaxHealth * retreatUntilHealthShare;
            if (healed || m_RetreatStation == null || !m_RetreatStation.IsSpawned)
            {
                m_RetreatStation = null;
                currentState = BotState.Idle;
                m_NextDecisionTime = Time.time;
                return;
            }

            float sqrRadius = m_RetreatStation.Radius * m_RetreatStation.Radius;
            if ((m_RetreatStation.transform.position - transform.position).sqrMagnitude > sqrRadius * 0.5f)
            {
                SetAgentSpeed(TravelSpeed);
                MoveToTarget(m_RetreatStation.transform.position);
            }
            else
            {
                StopAgent();
            }

            // Shoot back on the way if the enemy is in sight.
            if (perception.TargetVisible)
            {
                FaceDirection(perception.AimPoint - transform.position);
                if (Time.time - perception.TargetAcquiredTime >= reactionDelay) FireAt(perception.AimPoint);
            }
        }

        private ConquestSupportStation FindFriendlyStation()
        {
            ConquestSupportStation best = null;
            float bestSqr = retreatSearchRange * retreatSearchRange;
            var stations = ConquestSupportStation.Active;
            for (int i = 0; i < stations.Count; i++)
            {
                ConquestSupportStation station = stations[i];
                if (station == null || !station.IsSpawned || station.stationTeam.Value != m_Player.CurrentTeam) continue;
                float sqr = (station.transform.position - transform.position).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = station;
            }
            return best;
        }

        private ConquestBotClassRole CurrentRole
        {
            get
            {
                ConquestClassData data = m_Player.CurrentClassData;
                if (data != null && classRoles != null)
                {
                    for (int i = 0; i < classRoles.Length; i++)
                    {
                        if (classRoles[i].classType == data.classType) return classRoles[i];
                    }
                }
                return DefaultRole;
            }
        }

        private static readonly ConquestBotClassRole DefaultRole = ConquestBotClassRole.Defaults[0];

        // Class speeds, so a Heavy bot is slower than a Recon one, as for players.
        private float WalkSpeed => m_Player.CurrentClassData != null ? m_Player.CurrentClassData.walkSpeed : m_Agent.speed;
        private float TravelSpeed => m_Player.CurrentClassData != null ? m_Player.CurrentClassData.sprintSpeed * travelSpeedShare : m_Agent.speed;

        private void SetAgentSpeed(float speed)
        {
            if (m_Agent.isActiveAndEnabled) m_Agent.speed = speed;
        }

        private void StopAgent()
        {
            if (m_Agent.isActiveAndEnabled && m_Agent.isOnNavMesh) m_Agent.isStopped = true;
        }

        // Staggered perception: each bot looks around on its own short timer, so a full server
        // doesn't run every bot's line-of-sight checks in the same frame.
        private void TickPerception()
        {
            if (Time.time < m_NextPerceptionTime) return;
            m_NextPerceptionTime = Time.time + perceptionInterval;

            perception.Tick(m_Player, detectionRange, fieldOfView, visionMask);
            if (perception.HasTarget && currentState != BotState.EngageEnemy && currentState != BotState.Retreat)
            {
                currentState = BotState.EngageEnemy;
            }
        }

        // Shot by someone: turn on them even if they were behind the bot.
        private void HandleServerDamaged(ulong attackerObjectId, Vector3 sourcePosition)
        {
            if (m_Health.IsDead || NetworkManager == null) return;
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(attackerObjectId, out NetworkObject attackerObject)) return;
            if (!attackerObject.TryGetComponent(out ConquestPlayer attacker)) return;

            perception.NotifyAttackedBy(m_Player, attacker);
            if (perception.HasTarget && currentState != BotState.Retreat) currentState = BotState.EngageEnemy;
        }

        // Progress is measured between samples a fixed time apart, not between frames: one frame
        // of even a sprinting bot is a few centimetres, which used to look "stuck" all the time.
        private void CheckStuck()
        {
            if (Time.time < m_NextStuckSampleTime) return;
            m_NextStuckSampleTime = Time.time + stuckSampleInterval;

            Vector3 position = transform.position;
            float moved = Vector3.Distance(position, m_LastStuckSample);
            m_LastStuckSample = position;

            if (!WantsToMove() || moved >= stuckMinProgress)
            {
                m_StuckTimer = 0f;
                return;
            }

            m_StuckTimer += stuckSampleInterval;
            if (m_StuckTimer < stuckThresholdTime) return;

            m_StuckTimer = 0f;
            RecoverFromStuck();
        }

        // True when the agent has somewhere to go and nothing intends it to stand still.
        private bool WantsToMove()
        {
            if (!m_Agent.isActiveAndEnabled || !m_Agent.isOnNavMesh) return false;
            if (m_Agent.isStopped || m_Agent.pathPending || !m_Agent.hasPath) return false;
            return m_Agent.remainingDistance > m_Agent.stoppingDistance + 0.25f;
        }

        // Step to a random nearby point on the NavMesh to get off whatever is blocking the bot,
        // then carry on to the same destination once the detour is over.
        private void RecoverFromStuck()
        {
            Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(2f, 4f);
            Vector3 probe = transform.position + new Vector3(offset.x, 0f, offset.y);
            if (NavMesh.SamplePosition(probe, out NavMeshHit navHit, 3f, NavMesh.AllAreas))
            {
                m_Agent.isStopped = false;
                m_Agent.SetDestination(navHit.position);
                m_RecoverUntil = Time.time + stuckRecoverySeconds;
            }
        }

        // Called after a teleport or respawn so the jump isn't read as progress (or the lack of it).
        private void ResetStuckSampling()
        {
            m_StuckTimer = 0f;
            m_LastStuckSample = transform.position;
            m_NextStuckSampleTime = Time.time + stuckSampleInterval;
            m_RecoverUntil = 0f;
        }

        // The agent only ever runs on the server; clients keep theirs disabled for good.
        private void HandleDeath(ulong killerId)
        {
            if (!IsServer) return;
            currentState = BotState.Dead;
            perception.Clear();
            if (m_Agent.isActiveAndEnabled && m_Agent.isOnNavMesh)
            {
                m_Agent.isStopped = true;
                m_Agent.enabled = false;
            }
        }

        private void HandleRespawn()
        {
            if (!IsServer) return;
            if (m_Agent != null)
            {
                m_Agent.enabled = true;
            }
            currentState = BotState.Idle;
            perception.Clear();
            m_RetreatStation = null;
            m_NextDecisionTime = Time.time + Random.Range(0.2f, 0.6f);
            ResetStuckSampling();
        }
    }
}
