using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using Game.Conquest.Player;
using Game.Conquest.Combat;
using Game.Conquest.Objectives;

namespace Game.Conquest.Gameplay
{
    public enum BotState
    {
        Idle,
        MoveToObjective,
        Capture,
        Defend,
        EngageEnemy,
        Dead
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
        private ConquestPlayer m_CurrentEnemy;
        private float m_NextDecisionTime;
        private float m_NextFireCheckTime;
        private float m_TargetDetectedTime;
        private Vector3 m_LastStuckSample;
        private float m_NextStuckSampleTime;
        private float m_StuckTimer;
        private float m_RecoverUntil;
        private bool m_HasReacted;
        private float m_NextStationAttemptTime;
        private const float StationAttemptInterval = 3f;

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
                currentState = BotState.Idle;
                m_NextDecisionTime = Time.time + Random.Range(0.2f, 1.0f);
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
                    break;
                case BotDifficulty.Normal:
                    detectionRange = 45.0f;
                    fieldOfView = 135.0f;
                    aimAccuracy = 0.75f;
                    reactionDelay = 0.35f;
                    break;
                case BotDifficulty.Hard:
                    detectionRange = 60.0f;
                    fieldOfView = 160.0f;
                    aimAccuracy = 0.90f;
                    reactionDelay = 0.18f;
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
            }

            CheckStuck();
        }

        private void EvaluateBattlefield()
        {
            // 1. Scan for visible enemies
            ConquestPlayer nearestEnemy = ScanForEnemy();
            if (nearestEnemy != null)
            {
                if (m_CurrentEnemy != nearestEnemy)
                {
                    m_CurrentEnemy = nearestEnemy;
                    m_TargetDetectedTime = Time.time;
                    m_HasReacted = false;
                }
                currentState = BotState.EngageEnemy;
                return;
            }
            m_CurrentEnemy = null;

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
        public float DefendPreference => 0f;

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
                if (NavMesh.SamplePosition(guardSpot, out NavMeshHit hit, 3f, NavMesh.AllAreas)) MoveToTarget(hit.position);
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
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 180.0f * Time.deltaTime);
        }

        private void UpdateEngageEnemy()
        {
            if (m_CurrentEnemy == null || m_CurrentEnemy.Health.IsDead)
            {
                m_CurrentEnemy = null;
                currentState = BotState.Idle;
                return;
            }

            Vector3 toEnemy = m_CurrentEnemy.transform.position - transform.position;
            float dist = toEnemy.magnitude;

            // Turn towards enemy
            Vector3 lookDir = new Vector3(toEnemy.x, 0f, toEnemy.z);
            if (lookDir.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(lookDir),
                    180.0f * Time.deltaTime);
            }

            // Move closer if outside optimal range, or stop to shoot
            if (dist > 18.0f)
            {
                MoveToTarget(m_CurrentEnemy.transform.position);
            }
            else if (m_Agent.isActiveAndEnabled && m_Agent.isOnNavMesh)
            {
                m_Agent.isStopped = true;
            }

            // Reaction delay check
            if (!m_HasReacted)
            {
                if (Time.time - m_TargetDetectedTime >= reactionDelay)
                {
                    m_HasReacted = true;
                }
                else
                {
                    return;
                }
            }

            // Aim error is an angle, so a bot misses by more at long range, like a person would.
            Vector3 targetAimPoint = m_CurrentEnemy.transform.position + Vector3.up * 1.3f;
            float aimErrorDegrees = Mathf.Lerp(worstAimErrorDegrees, bestAimErrorDegrees, aimAccuracy);

            if (m_Weapon != null)
            {
                m_Weapon.FireFromAI(targetAimPoint, aimErrorDegrees);
            }
        }

        private ConquestPlayer ScanForEnemy()
        {
            ConquestPlayer closest = null;
            float closestSqrDist = detectionRange * detectionRange;
            Vector3 eyePos = transform.position + Vector3.up * 1.6f;

            // The roster of spawned players, not an OverlapSphere: no allocation, and range is
            // measured to the target's position rather than to whichever collider overlapped.
            var all = ConquestPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                ConquestPlayer candidate = all[i];
                if (candidate == null || candidate == m_Player || candidate.Health.IsDead) continue;
                if (candidate.CurrentTeam == m_Player.CurrentTeam || candidate.CurrentTeam == Team.Neutral) continue;

                Vector3 toTarget = candidate.transform.position - transform.position;
                float sqrDist = toTarget.sqrMagnitude;
                if (sqrDist > closestSqrDist) continue;
                if (Vector3.Angle(transform.forward, toTarget) > fieldOfView * 0.5f) continue;

                // Line of sight: the bot's own body never blocks it, and hitting the target counts as seeing it.
                Vector3 targetPos = candidate.transform.position + Vector3.up * 1.3f;
                if (ConquestPhysics.LinecastIgnoring(transform, eyePos, targetPos, visionMask, out RaycastHit hit) &&
                    !hit.collider.transform.IsChildOf(candidate.transform))
                {
                    continue;
                }

                closestSqrDist = sqrDist;
                closest = candidate;
            }

            return closest;
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
            m_CurrentEnemy = null;
            m_NextDecisionTime = Time.time + Random.Range(0.2f, 0.6f);
            ResetStuckSampling();
        }
    }
}
