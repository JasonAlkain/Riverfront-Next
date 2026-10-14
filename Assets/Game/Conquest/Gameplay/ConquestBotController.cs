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
        [SerializeField] private LayerMask visionMask = ~0;

        [Header("Behavior Intervals")]
        [SerializeField] private float decisionInterval = 1.0f;
        [SerializeField] private float stuckThresholdTime = 2.5f;

        private NavMeshAgent m_Agent;
        private ConquestPlayer m_Player;
        private ConquestHealth m_Health;
        private ConquestWeapon m_Weapon;
        private ConquestPlayerAnimator m_Animator;

        private ConquestCommandPost m_TargetPost;
        private ConquestPlayer m_CurrentEnemy;
        private float m_NextDecisionTime;
        private float m_NextFireCheckTime;
        private float m_TargetDetectedTime;
        private Vector3 m_LastPos;
        private float m_StuckTimer;
        private bool m_HasReacted;

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
            else
            {
                m_CurrentEnemy = null;
            }

            // 2. If we are currently engaging an enemy but lost line of sight/enemy died, return to objective
            if (currentState == BotState.EngageEnemy)
            {
                currentState = BotState.Idle;
            }

            // 3. Check current objective status: push to next capture point if current is already captured & uncontested
            if (m_TargetPost != null)
            {
                // If our team already owns this post fully and it's not contested, push to next objective!
                if (m_TargetPost.CurrentOwner == m_Player.CurrentTeam && !m_TargetPost.IsContested && m_TargetPost.CaptureProgress >= 0.99f)
                {
                    SelectBestObjective();
                }
                else
                {
                    float distToPost = Vector3.Distance(transform.position, m_TargetPost.transform.position);
                    if (distToPost <= m_TargetPost.CaptureRadius * 0.9f)
                    {
                        if (m_TargetPost.CurrentOwner == m_Player.CurrentTeam && !m_TargetPost.IsContested)
                        {
                            // Already secured, select next objective to push forward
                            SelectBestObjective();
                        }
                        else
                        {
                            currentState = BotState.Capture;
                            return;
                        }
                    }
                }
            }
            else
            {
                SelectBestObjective();
            }

            // 4. Move to selected objective
            if (m_TargetPost != null)
            {
                currentState = BotState.MoveToObjective;
                Vector3 dest = m_TargetPost.GetCaptureTargetPosition(transform.position);
                MoveToTarget(dest);
            }
            else
            {
                currentState = BotState.Idle;
            }
        }

        private void SelectBestObjective()
        {
            var mm = ConquestMatchManager.Instance;
            if (mm == null || mm.CommandPosts == null || mm.CommandPosts.Count == 0) return;

            ConquestCommandPost bestTarget = null;
            float bestScore = float.MinValue;

            foreach (var post in mm.CommandPosts)
            {
                if (post == null) continue;

                float dist = Vector3.Distance(transform.position, post.transform.position);
                float score = -dist * 0.8f; // Distance weight

                // High priority: Uncaptured / Enemy command posts to continuously push forward
                if (post.CurrentOwner != m_Player.CurrentTeam)
                {
                    score += 150.0f; // Strongly incentivize pushing enemy / neutral points
                    if (post.CurrentOwner == Team.Neutral)
                    {
                        score += 50.0f; // Neutral points are easy grabs
                    }
                }
                else
                {
                    // Only stay or look back at a friendly post if it is actively being captured or contested!
                    if (post.IsContested)
                    {
                        score += 120.0f;
                    }
                    else if (post.CaptureProgress < 1.0f)
                    {
                        score += 70.0f;
                    }
                    else
                    {
                        // Already 100% captured friendly post: very low score so bots don't linger
                        score -= 200.0f;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = post;
                }
            }

            m_TargetPost = bestTarget;
        }

        private void MoveToTarget(Vector3 destination)
        {
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

            float dist = Vector3.Distance(transform.position, m_TargetPost.transform.position);
            if (dist <= m_TargetPost.CaptureRadius * 0.85f)
            {
                if (m_TargetPost.CurrentOwner == m_Player.CurrentTeam && !m_TargetPost.IsContested && m_TargetPost.CaptureProgress >= 0.99f)
                {
                    // Secured, select next objective to push forward
                    SelectBestObjective();
                    if (m_TargetPost != null)
                    {
                        MoveToTarget(m_TargetPost.GetCaptureTargetPosition(transform.position));
                    }
                }
                else
                {
                    currentState = BotState.Capture;
                }
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
            if (dist > m_TargetPost.CaptureRadius * 0.9f || dist < 2.0f)
            {
                MoveToTarget(m_TargetPost.GetCaptureTargetPosition(transform.position));
            }

            // Once fully captured and secured, push immediately to next objective!
            if (m_TargetPost.CurrentOwner == m_Player.CurrentTeam && !m_TargetPost.IsContested && m_TargetPost.CaptureProgress >= 0.99f)
            {
                SelectBestObjective();
                if (m_TargetPost != null)
                {
                    currentState = BotState.MoveToObjective;
                    MoveToTarget(m_TargetPost.GetCaptureTargetPosition(transform.position));
                }
            }
        }

        private void UpdateDefend()
        {
            if (m_TargetPost == null)
            {
                currentState = BotState.Idle;
                return;
            }

            // If post is secured and uncontested, push to the next point
            if (m_TargetPost.CurrentOwner == m_Player.CurrentTeam && !m_TargetPost.IsContested && m_TargetPost.CaptureProgress >= 0.99f)
            {
                SelectBestObjective();
                if (m_TargetPost != null)
                {
                    currentState = BotState.MoveToObjective;
                    MoveToTarget(m_TargetPost.GetCaptureTargetPosition(transform.position));
                }
                return;
            }

            // If enemy started capturing, contest it
            if (m_TargetPost.CurrentOwner != m_Player.CurrentTeam || m_TargetPost.IsContested)
            {
                currentState = BotState.Capture;
            }
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

            // Aim with simulated inaccuracy
            Vector3 targetAimPoint = m_CurrentEnemy.transform.position + Vector3.up * 1.3f;
            float maxError = Mathf.Lerp(2.2f, 0.2f, aimAccuracy);
            Vector3 randomOffset = Random.insideUnitSphere * maxError;
            targetAimPoint += randomOffset;

            // Fire weapon
            if (m_Weapon != null)
            {
                m_Weapon.FireFromAI(targetAimPoint);
            }
        }

        private ConquestPlayer ScanForEnemy()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, detectionRange);
            ConquestPlayer closest = null;
            float closestDist = float.MaxValue;

            foreach (var col in hits)
            {
                ConquestPlayer candidate = col.GetComponentInParent<ConquestPlayer>();
                if (candidate == null || candidate == m_Player || candidate.Health.IsDead) continue;
                if (candidate.CurrentTeam == m_Player.CurrentTeam || candidate.CurrentTeam == Team.Neutral) continue;

                Vector3 toTarget = candidate.transform.position - transform.position;
                float angle = Vector3.Angle(transform.forward, toTarget);

                // FOV check
                if (angle <= fieldOfView * 0.5f)
                {
                    // Line of sight check
                    Vector3 eyePos = transform.position + Vector3.up * 1.6f;
                    Vector3 targetPos = candidate.transform.position + Vector3.up * 1.3f;

                    if (!Physics.Linecast(eyePos, targetPos, out RaycastHit hit, visionMask, QueryTriggerInteraction.Ignore) ||
                        hit.collider.transform.IsChildOf(candidate.transform))
                    {
                        float dist = toTarget.sqrMagnitude;
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closest = candidate;
                        }
                    }
                }
            }

            return closest;
        }

        private void CheckStuck()
        {
            if (currentState == BotState.MoveToObjective)
            {
                float moved = Vector3.Distance(transform.position, m_LastPos);
                if (moved < 0.2f)
                {
                    m_StuckTimer += Time.deltaTime;
                    if (m_StuckTimer >= stuckThresholdTime)
                    {
                        m_StuckTimer = 0f;
                        // Recover: re-sample NavMesh and pick next objective
                        if (NavMesh.SamplePosition(transform.position + Random.insideUnitSphere * 3f, out NavMeshHit navHit, 5f, NavMesh.AllAreas))
                        {
                            MoveToTarget(navHit.position);
                        }
                        SelectBestObjective();
                    }
                }
                else
                {
                    m_StuckTimer = 0f;
                }
            }
            m_LastPos = transform.position;
        }

        private void HandleDeath(ulong killerId)
        {
            currentState = BotState.Dead;
            if (m_Agent.isActiveAndEnabled && m_Agent.isOnNavMesh)
            {
                m_Agent.isStopped = true;
                m_Agent.enabled = false;
            }
        }

        private void HandleRespawn()
        {
            if (m_Agent != null)
            {
                m_Agent.enabled = true;
            }
            currentState = BotState.Idle;
            m_CurrentEnemy = null;
            m_NextDecisionTime = Time.time + Random.Range(0.2f, 0.6f);
        }
    }
}
