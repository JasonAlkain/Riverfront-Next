using System.Collections.Generic;
using Unity.Netcode;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.Conquest.Gameplay;
using Game.Conquest.Player;

namespace Game.Conquest.Combat
{
    /// <summary>
    /// Server only: every bolt in flight, and the one place bolts hit things. Bolts are plain
    /// structs (not NetworkObjects), stepped at the fixed rate along a straight line and swept
    /// between steps, so nothing tunnels. Clients only draw bolts (ConquestBoltPool); damage is
    /// decided here.
    /// </summary>
    public class ConquestBoltSimulator : MonoBehaviour
    {
        // Placeholder: starting list size. It grows when full; an authoritative bolt is never dropped.
        private const int InitialCapacity = 1024;

        private struct ServerBolt
        {
            public Vector3 origin;
            public Vector3 dir;
            public double t0;
            public float speed;
            public float maxDist;
            public float traveled;
            public float radius;
            public int damage;
            public int mask;
            public Team team;
            public ulong shooterId;
            public ConquestWeapon shooter;
            public Transform ignoreRoot;
            public ushort seq;
            public byte pellet;
        }

        private static ConquestBoltSimulator s_Instance;

        private readonly List<ServerBolt> m_Bolts = new List<ServerBolt>(InitialCapacity);
        private bool m_Stepping;
        private bool m_ClearRequested;
        private bool m_WarnedGrow;
        private int m_SyncedFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Instance = null;
        }

        private static ConquestBoltSimulator Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    var root = new GameObject("ConquestBoltSimulator");
                    DontDestroyOnLoad(root);
                    s_Instance = root.AddComponent<ConquestBoltSimulator>();
                }
                return s_Instance;
            }
        }

        /// <summary>Bolts currently in flight (diagnostics).</summary>
        public static int ActiveCount => s_Instance != null ? s_Instance.m_Bolts.Count : 0;

        /// <summary>
        /// Server: fire one bolt. <paramref name="fastForward"/> seconds of flight are covered at
        /// once, so a remote player's bolt starts where their own screen already shows it.
        /// </summary>
        public static void Launch(ConquestWeapon shooter, Vector3 origin, Vector3 dir, float speed, float maxDist, float radius,
            int damage, int mask, Team team, ushort seq, byte pellet, float fastForward)
        {
            var sim = Instance;
            var bolt = new ServerBolt
            {
                origin = origin,
                dir = dir.normalized,
                t0 = Time.timeAsDouble - fastForward,
                speed = Mathf.Max(0.1f, speed),
                maxDist = Mathf.Max(0f, maxDist),
                radius = Mathf.Max(0f, radius),
                damage = damage,
                mask = mask,
                team = team,
                shooterId = shooter != null ? shooter.NetworkObjectId : ConquestHealth.NoAttacker,
                shooter = shooter,
                ignoreRoot = shooter != null ? shooter.transform : null,
                seq = seq,
                pellet = pellet
            };

            if (fastForward > 0f)
            {
                sim.EnsureSynced();
                if (sim.Sweep(ref bolt, Mathf.Min(bolt.maxDist, bolt.speed * fastForward))) return;
            }
            if (bolt.traveled >= bolt.maxDist) return;

            if (sim.m_Bolts.Count == sim.m_Bolts.Capacity && !sim.m_WarnedGrow)
            {
                sim.m_WarnedGrow = true;
                Debug.LogWarning($"[ConquestBoltSimulator] More than {sim.m_Bolts.Capacity} bolts in flight; growing the list.");
            }
            sim.m_Bolts.Add(bolt);
        }

        /// <summary>Server: drop every bolt in flight (match end, scene change). Safe to call from inside a hit.</summary>
        public static void ServerClearAll()
        {
            if (s_Instance == null) return;
            if (s_Instance.m_Stepping) s_Instance.m_ClearRequested = true;
            else s_Instance.m_Bolts.Clear();
        }

        private void OnEnable()
        {
            SceneManager.activeSceneChanged += HandleSceneChanged;
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= HandleSceneChanged;
        }

        private void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
        }

        private void HandleSceneChanged(Scene from, Scene to)
        {
            ServerClearAll();
        }

        // Fixed rate rather than every frame: an uncapped listen-server host would otherwise
        // multiply the casts.
        // Profiler sample name read by the network test driver (Tools/NetTest).
        private static readonly ProfilerMarker s_StepMarker = new ProfilerMarker("Conquest.BoltSim");

        private void FixedUpdate()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer)
            {
                if (m_Bolts.Count > 0) m_Bolts.Clear();
                return;
            }
            if (m_Bolts.Count == 0) return;
            using var _ = s_StepMarker.Auto();

            EnsureSynced();
            double now = Time.timeAsDouble;
            m_Stepping = true;
            // Backwards, so removing a bolt by swapping in the last one never skips a bolt.
            for (int i = m_Bolts.Count - 1; i >= 0 && !m_ClearRequested; i--)
            {
                if (i >= m_Bolts.Count) continue;
                ServerBolt bolt = m_Bolts[i];
                float distance = Mathf.Min(bolt.maxDist, bolt.speed * (float)(now - bolt.t0));
                bool ended = Sweep(ref bolt, distance) || bolt.traveled >= bolt.maxDist - 1e-3f;
                if (m_ClearRequested) break;
                if (ended)
                {
                    int last = m_Bolts.Count - 1;
                    m_Bolts[i] = m_Bolts[last];
                    m_Bolts.RemoveAt(last);
                }
                else
                {
                    m_Bolts[i] = bolt;
                }
            }
            m_Stepping = false;
            if (m_ClearRequested)
            {
                m_ClearRequested = false;
                m_Bolts.Clear();
            }
        }

        // Auto sync transforms is off, so colliders moved by network transforms and nav agents
        // since the last physics step would otherwise be tested where they were.
        private void EnsureSynced()
        {
            if (m_SyncedFrame == Time.frameCount) return;
            m_SyncedFrame = Time.frameCount;
            Physics.SyncTransforms();
        }

        /// <summary>Sweep a bolt from where it was to <paramref name="toDistance"/>. True if it hit something (and is spent).</summary>
        private bool Sweep(ref ServerBolt bolt, float toDistance)
        {
            float length = toDistance - bolt.traveled;
            if (length <= 0f) return false;
            Vector3 start = bolt.origin + bolt.dir * bolt.traveled;
            bolt.traveled = toDistance;

            RaycastHit hit;
            bool didHit;
            if (bolt.radius <= 0f)
            {
                didHit = ConquestPhysics.RaycastIgnoring(bolt.ignoreRoot, start, bolt.dir, length, bolt.mask, out hit);
            }
            else
            {
                // Thick bolts: walls stop them exactly, characters are found by the sphere.
                bool wall = ConquestPhysics.RaycastIgnoring(bolt.ignoreRoot, start, bolt.dir, length, bolt.mask & ConquestPhysics.WorldMask, out RaycastHit wallHit);
                float limit = wall ? wallHit.distance : length;
                bool character = ConquestPhysics.SphereCastIgnoring(bolt.ignoreRoot, start, bolt.radius, bolt.dir, limit,
                    bolt.mask & ConquestPhysics.CharacterMask, out RaycastHit characterHit);
                didHit = wall || character;
                hit = character ? characterHit : wallHit;
            }

            if (!didHit) return false;
            ApplyHit(bolt, hit);
            return true;
        }

        // The rules from the old hitscan shot, unchanged: no friendly fire (a teammate or a friendly
        // station soaks the bolt), enemy players and stations take damage, the world stops it.
        // Supply droids take damage from any team they don't exclusively serve.
        private static void ApplyHit(in ServerBolt bolt, RaycastHit hit)
        {
            if (!ConquestPhysics.TryGetDamageable(hit.collider, out ConquestHealth targetHealth,
                    out ConquestSupportStation station, out ConquestSupplyDroid droid)) return;

            if (targetHealth != null)
            {
                ConquestPlayer targetPlayer = targetHealth.GetComponent<ConquestPlayer>();
                if (targetPlayer == null || targetPlayer.CurrentTeam != bolt.team)
                {
                    bool wasAlive = !targetHealth.IsDead;
                    targetHealth.TakeDamage(bolt.damage, bolt.shooterId, bolt.origin, hit.point, bolt.seq, bolt.pellet);
                    if (bolt.shooter != null) bolt.shooter.ServerNoteHit(wasAlive && targetHealth.IsDead);
                }
                return;
            }

            if (station != null && station.stationTeam.Value != bolt.team)
            {
                station.TakeDamage(bolt.damage);
                if (bolt.shooter != null) bolt.shooter.ServerNoteHit(false);
                return;
            }

            if (droid != null && !droid.IsBroken && droid.CanBeDamagedBy(bolt.team))
            {
                droid.TakeDamage(bolt.damage);
                if (bolt.shooter != null) bolt.shooter.ServerNoteHit(false);
            }
        }
    }
}
