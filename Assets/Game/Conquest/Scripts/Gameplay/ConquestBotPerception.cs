using System;
using Unity.Profiling;
using UnityEngine;
using Game.Conquest.Combat;
using Game.Conquest.Player;

namespace Game.Conquest.Gameplay
{
    /// <summary>
    /// Server: what a bot knows about its enemies. It sees within its range and field of view,
    /// hears nearby gunfire from any direction, turns on whoever shoots it, and remembers a target
    /// for a few seconds after losing sight of it. Enemy stations are targets when no player is.
    /// Allocation-free: it walks the player and station rosters instead of querying physics overlaps.
    /// </summary>
    [Serializable]
    public class ConquestBotPerception
    {
        // Profiler sample name read by the network test driver (Tools/NetTest).
        private static readonly ProfilerMarker s_TickMarker = new ProfilerMarker("Conquest.BotPerception");

        [Tooltip("Placeholder: metres within which an enemy's gunfire is heard from any direction.")]
        [SerializeField] private float hearingRange = 25f;
        [Tooltip("Placeholder: seconds after a shot during which the shooter still counts as heard.")]
        [SerializeField] private float shotHeardSeconds = 1f;
        [Tooltip("Placeholder: seconds a bot keeps hunting an enemy it can no longer see.")]
        [SerializeField] private float memorySeconds = 3f;
        [Tooltip("Placeholder: metres closer a newly seen enemy must be before the bot switches to it.")]
        [SerializeField] private float switchTargetMargin = 5f;

        private const float EyeHeight = 1.6f;
        private const float ChestHeight = 1.3f;
        private const float StationAimHeight = 0.5f;

        private float m_LastSeenTime;

        /// <summary>The enemy player being fought or hunted, or null.</summary>
        public ConquestPlayer Enemy { get; private set; }
        /// <summary>An enemy station in sight, only while no player is targeted.</summary>
        public ConquestSupportStation Station { get; private set; }
        /// <summary>The target was in line of sight on the latest tick, so shooting at it can hit.</summary>
        public bool TargetVisible { get; private set; }
        /// <summary>Where the target was last seen (or where a hit came from).</summary>
        public Vector3 LastKnownPosition { get; private set; }
        /// <summary>When the current target was first noticed; the bot's reaction delay counts from here.</summary>
        public float TargetAcquiredTime { get; private set; }

        /// <summary>
        /// The visible enemy's velocity as the server sees it, sampled once per tick (zero for
        /// stations and targets out of sight). A target that changes direction between ticks dodges.
        /// </summary>
        public Vector3 EnemyVelocity { get; private set; }

        public bool HasTarget => Enemy != null || Station != null;
        /// <summary>Seconds since the target was last in sight (or last hit this bot).</summary>
        public float SecondsSinceSeen => Time.time - m_LastSeenTime;

        /// <summary>Where to aim: the target's chest (or a station's body), or where it was last seen.</summary>
        public Vector3 AimPoint
        {
            get
            {
                if (Enemy != null && TargetVisible) return Enemy.transform.position + Vector3.up * ChestHeight;
                if (Station != null) return Station.transform.position + Vector3.up * StationAimHeight;
                return LastKnownPosition + Vector3.up * ChestHeight;
            }
        }

        public void Clear()
        {
            Enemy = null;
            Station = null;
            TargetVisible = false;
            EnemyVelocity = Vector3.zero;
        }

        /// <summary>Server: the bot was hit by <paramref name="attacker"/>. It turns on them, whatever it was doing.</summary>
        public void NotifyAttackedBy(ConquestPlayer self, ConquestPlayer attacker)
        {
            if (!IsLivingEnemy(self, attacker)) return;
            if (Enemy == attacker && TargetVisible) return;

            if (Enemy != attacker) TargetAcquiredTime = Time.time;
            Enemy = attacker;
            Station = null;
            TargetVisible = false;
            EnemyVelocity = Vector3.zero;
            LastKnownPosition = attacker.transform.position;
            m_LastSeenTime = Time.time;
        }

        /// <summary>Server: one perception update. Call it a few times a second, not every frame.</summary>
        public void Tick(ConquestPlayer self, float range, float fieldOfView, int visionMask)
        {
            using var _ = s_TickMarker.Auto();
            Transform eye = self.transform;
            Vector3 eyePos = eye.position + Vector3.up * EyeHeight;

            // Keep tracking the current enemy: no field-of-view check, the bot is already on it.
            if (Enemy != null)
            {
                if (!IsLivingEnemy(self, Enemy))
                {
                    Clear();
                }
                else
                {
                    TargetVisible = (Enemy.transform.position - eye.position).sqrMagnitude <= range * range &&
                                    CanSee(eye, eyePos, Enemy.transform, Enemy.transform.position + Vector3.up * ChestHeight, visionMask);
                    if (TargetVisible)
                    {
                        LastKnownPosition = Enemy.transform.position;
                        m_LastSeenTime = Time.time;
                    }
                }
            }

            ConquestPlayer spotted = FindNewEnemy(self, eyePos, range, fieldOfView, visionMask);
            if (spotted != null && spotted != Enemy && ShouldSwitchTo(self, spotted))
            {
                Enemy = spotted;
                Station = null;
                TargetVisible = true;
                LastKnownPosition = spotted.transform.position;
                m_LastSeenTime = Time.time;
                TargetAcquiredTime = Time.time;
            }

            if (Enemy != null)
            {
                if (!TargetVisible && Time.time - m_LastSeenTime > memorySeconds) Clear();
                if (Enemy != null)
                {
                    EnemyVelocity = TargetVisible && Enemy.Movement != null ? Enemy.Movement.ServerVelocity : Vector3.zero;
                    return;
                }
            }
            EnemyVelocity = Vector3.zero;

            // Nobody to fight: shoot at an enemy station in sight instead.
            ConquestSupportStation station = FindEnemyStation(self, eyePos, range, fieldOfView, visionMask);
            if (station != Station && station != null) TargetAcquiredTime = Time.time;
            Station = station;
            TargetVisible = station != null;
            if (station != null) LastKnownPosition = station.transform.position;
        }

        // A visible new enemy wins over one that has slipped out of sight, or over a visible one
        // that is clearly further away.
        private bool ShouldSwitchTo(ConquestPlayer self, ConquestPlayer candidate)
        {
            if (Enemy == null || !TargetVisible) return true;
            Vector3 me = self.transform.position;
            float current = Vector3.Distance(me, Enemy.transform.position);
            float next = Vector3.Distance(me, candidate.transform.position);
            return next + switchTargetMargin < current;
        }

        private ConquestPlayer FindNewEnemy(ConquestPlayer self, Vector3 eyePos, float range, float fieldOfView, int visionMask)
        {
            Transform eye = self.transform;
            ConquestPlayer closest = null;
            float closestSqr = range * range;
            float hearingSqr = hearingRange * hearingRange;

            var all = ConquestPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                ConquestPlayer candidate = all[i];
                if (!IsLivingEnemy(self, candidate)) continue;

                Vector3 toTarget = candidate.transform.position - eye.position;
                float sqr = toTarget.sqrMagnitude;
                if (sqr > closestSqr) continue;

                // Out of the view cone, an enemy is only noticed if it just fired close by.
                bool inView = Vector3.Angle(eye.forward, toTarget) <= fieldOfView * 0.5f;
                if (!inView && !(sqr <= hearingSqr && RecentlyFired(candidate))) continue;

                if (!CanSee(eye, eyePos, candidate.transform, candidate.transform.position + Vector3.up * ChestHeight, visionMask)) continue;

                closestSqr = sqr;
                closest = candidate;
            }
            return closest;
        }

        private ConquestSupportStation FindEnemyStation(ConquestPlayer self, Vector3 eyePos, float range, float fieldOfView, int visionMask)
        {
            Transform eye = self.transform;
            ConquestSupportStation closest = null;
            float closestSqr = range * range;

            var stations = ConquestSupportStation.Active;
            for (int i = 0; i < stations.Count; i++)
            {
                ConquestSupportStation station = stations[i];
                if (station == null || !station.IsSpawned) continue;
                Team team = station.stationTeam.Value;
                if (team == self.CurrentTeam || team == Team.Neutral) continue;

                Vector3 toTarget = station.transform.position - eye.position;
                float sqr = toTarget.sqrMagnitude;
                if (sqr > closestSqr) continue;
                if (Vector3.Angle(eye.forward, toTarget) > fieldOfView * 0.5f) continue;
                if (!CanSee(eye, eyePos, station.transform, station.transform.position + Vector3.up * StationAimHeight, visionMask)) continue;

                closestSqr = sqr;
                closest = station;
            }
            return closest;
        }

        private bool RecentlyFired(ConquestPlayer player)
        {
            return player.Weapon != null && Time.time - player.Weapon.ServerLastShotTime <= shotHeardSeconds;
        }

        private static bool IsLivingEnemy(ConquestPlayer self, ConquestPlayer other)
        {
            if (other == null || other == self || other.Health == null || other.Health.IsDead) return false;
            return other.CurrentTeam != self.CurrentTeam && other.CurrentTeam != Team.Neutral;
        }

        // Line of sight: the bot's own body never blocks it, and hitting the target counts as seeing it.
        private static bool CanSee(Transform self, Vector3 eyePos, Transform target, Vector3 targetPoint, int visionMask)
        {
            if (!ConquestPhysics.LinecastIgnoring(self, eyePos, targetPoint, visionMask, out RaycastHit hit)) return true;
            return hit.collider.transform.IsChildOf(target);
        }
    }
}
