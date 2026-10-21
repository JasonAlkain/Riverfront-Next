using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.Conquest.Networking;

namespace Game.Conquest.Combat
{
    /// <summary>One bolt to draw. Purely visual: the server decides what it hits.</summary>
    public struct BoltVisual
    {
        /// <summary>Start of the true flight line (the shooter's aim ray).</summary>
        public Vector3 origin;
        public Vector3 dir;
        /// <summary>This peer's muzzle minus <see cref="origin"/>: drawn bolts start at the gun and merge onto the line.</summary>
        public Vector3 muzzleOffset;
        public float speed;
        public float maxDistance;
        public float width;
        public float length;
        public Material material;
        /// <summary>The local player's own shot: drawn on the local clock and ended early on a cosmetic character hit.</summary>
        public bool own;
        /// <summary>Fire time: local Time.timeAsDouble for own bolts, server time for everyone else's.</summary>
        public double t0;
        public ulong shooterId;
        public ushort seq;
        public byte pellet;
        public Transform ignoreRoot;
        public GameObject surfaceImpact;
        public GameObject characterImpact;
        public float impactSeconds;
    }

    /// <summary>
    /// Draws travelling bolts from a fixed ring of LineRenderers reused for the whole session, so
    /// sustained fire allocates nothing. Wall ends are found once at spawn (the map doesn't move);
    /// character hits come from the server through <see cref="EndPellet"/>. Another player's bolt
    /// is aged on server time and eases out of their muzzle to its true position. Never runs on a
    /// dedicated server.
    /// </summary>
    public class ConquestBoltPool : MonoBehaviour
    {
        // Placeholder: most bolts drawn at once; the oldest is recycled when full.
        private const int Capacity = 512;

        private struct Slot
        {
            public bool active;
            public BoltVisual v;
            public float endDistance;
            public Vector3 endPoint;
            public Vector3 endNormal;
            public bool endsOnSurface;
            public bool endsOnCharacter;
            public float catchUpAge;
            public float spawnRealTime;
            public float prevDistance;
        }

        private static ConquestBoltPool s_Instance;

        private readonly Slot[] m_Slots = new Slot[Capacity];
        private readonly LineRenderer[] m_Lines = new LineRenderer[Capacity];
        private int m_Next;
        private float m_ConvergeDistance = 8f;
        private float m_CatchUpSeconds = 0.12f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Instance = null;
        }

        /// <summary>Presentation tuning from the bolt settings asset.</summary>
        public static void Configure(float convergeDistance, float catchUpSeconds)
        {
            if (s_Instance == null) return;
            s_Instance.m_ConvergeDistance = Mathf.Max(0.01f, convergeDistance);
            s_Instance.m_CatchUpSeconds = Mathf.Max(0f, catchUpSeconds);
        }

        public static void Spawn(in BoltVisual bolt)
        {
            if (bolt.material == null || ConquestServerLaunchArgs.IsDedicatedServerLaunch()) return;
            if (s_Instance == null)
            {
                var root = new GameObject("ConquestBoltPool");
                DontDestroyOnLoad(root);
                s_Instance = root.AddComponent<ConquestBoltPool>();
            }
            s_Instance.SpawnBolt(bolt);
        }

        /// <summary>The server says this pellet hit a character at <paramref name="hitPoint"/>: end the drawn bolt there.</summary>
        public static void EndPellet(ulong shooterId, ushort seq, byte pellet, Vector3 hitPoint)
        {
            if (s_Instance == null) return;
            var slots = s_Instance.m_Slots;
            for (int i = 0; i < Capacity; i++)
            {
                ref Slot s = ref slots[i];
                if (!s.active || s.v.shooterId != shooterId || s.v.seq != seq || s.v.pellet != pellet) continue;
                float along = Mathf.Max(0f, Vector3.Dot(hitPoint - s.v.origin, s.v.dir));
                s.endsOnCharacter = true;
                s.endsOnSurface = false;
                s.endPoint = hitPoint;
                if (s.prevDistance >= along) s_Instance.Finish(i, true);
                else s.endDistance = along;
                return;
            }
        }

        /// <summary>The server refused this shot: remove its bolts without an impact.</summary>
        public static void EndShot(ulong shooterId, ushort seq)
        {
            if (s_Instance == null) return;
            for (int i = 0; i < Capacity; i++)
            {
                if (s_Instance.m_Slots[i].active && s_Instance.m_Slots[i].v.shooterId == shooterId && s_Instance.m_Slots[i].v.seq == seq)
                {
                    s_Instance.Finish(i, false);
                }
            }
        }

        public static void ClearAll()
        {
            if (s_Instance == null) return;
            for (int i = 0; i < Capacity; i++) s_Instance.Finish(i, false);
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
            ClearAll();
        }

        private void SpawnBolt(in BoltVisual bolt)
        {
            int index = m_Next;
            m_Next = (m_Next + 1) % Capacity;
            if (m_Slots[index].active) Finish(index, false);

            ref Slot s = ref m_Slots[index];
            s = default;
            s.active = true;
            s.v = bolt;
            s.v.dir = bolt.dir.normalized;
            s.endDistance = bolt.maxDistance;
            s.endPoint = bolt.origin + s.v.dir * bolt.maxDistance;
            s.spawnRealTime = Time.unscaledTime;
            if (ConquestPhysics.RaycastIgnoring(bolt.ignoreRoot, bolt.origin, s.v.dir, bolt.maxDistance, ConquestPhysics.WorldMask, out RaycastHit hit))
            {
                s.endDistance = hit.distance;
                s.endPoint = hit.point;
                s.endNormal = hit.normal;
                s.endsOnSurface = true;
            }
            if (!bolt.own) s.catchUpAge = Mathf.Max(0f, (float)(ServerNow() - bolt.t0));

            LineRenderer line = m_Lines[index];
            if (line == null)
            {
                var go = new GameObject("Bolt");
                go.transform.SetParent(transform, false);
                line = go.AddComponent<LineRenderer>();
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                m_Lines[index] = line;
            }
            line.sharedMaterial = bolt.material;
            line.startWidth = bolt.width;
            line.endWidth = bolt.width;
            line.enabled = false;
        }

        private static double ServerNow()
        {
            var nm = NetworkManager.Singleton;
            return nm != null && nm.IsListening ? nm.ServerTime.Time : Time.timeAsDouble;
        }

        private void Update()
        {
            double localNow = Time.timeAsDouble;
            double serverNow = ServerNow();
            float realNow = Time.unscaledTime;

            for (int i = 0; i < Capacity; i++)
            {
                ref Slot s = ref m_Slots[i];
                if (!s.active) continue;

                float age;
                if (s.v.own)
                {
                    age = (float)(localNow - s.v.t0);
                }
                else
                {
                    // Arrives already some way along; ease out of the muzzle to the true position.
                    float catchUp = m_CatchUpSeconds > 0f ? Mathf.SmoothStep(0f, 1f, (realNow - s.spawnRealTime) / m_CatchUpSeconds) : 1f;
                    age = (float)(serverNow - s.v.t0) - s.catchUpAge * (1f - catchUp);
                }

                float distance = s.v.speed * age;
                if (distance < 0f)
                {
                    // Not fired yet on this clock.
                    m_Lines[i].enabled = false;
                    continue;
                }

                if (s.v.own && distance > s.prevDistance && !s.endsOnCharacter)
                {
                    // Own shots only: an immediate cosmetic hit on a character. The hitmarker still
                    // comes from the server.
                    float step = Mathf.Min(distance, s.endDistance) - s.prevDistance;
                    if (step > 0f && ConquestPhysics.RaycastIgnoring(s.v.ignoreRoot, s.v.origin + s.v.dir * s.prevDistance, s.v.dir, step,
                            ConquestPhysics.CharacterMask, out RaycastHit hit))
                    {
                        s.endDistance = s.prevDistance + hit.distance;
                        s.endPoint = hit.point;
                        s.endsOnCharacter = true;
                        s.endsOnSurface = false;
                    }
                }

                if (distance >= s.endDistance)
                {
                    Finish(i, true);
                    continue;
                }

                float tailDistance = Mathf.Max(0f, distance - s.v.length);
                LineRenderer line = m_Lines[i];
                line.SetPosition(0, PointAt(s, tailDistance));
                line.SetPosition(1, PointAt(s, distance));
                if (!line.enabled) line.enabled = true;
                s.prevDistance = distance;
            }
        }

        // On the flight line, pulled toward this peer's muzzle near the start so the bolt leaves the gun.
        private Vector3 PointAt(in Slot s, float distance)
        {
            float pull = Mathf.Clamp01(1f - distance / m_ConvergeDistance);
            return s.v.origin + s.v.dir * distance + s.v.muzzleOffset * pull;
        }

        private void Finish(int index, bool impact)
        {
            ref Slot s = ref m_Slots[index];
            if (!s.active) return;
            s.active = false;
            if (m_Lines[index] != null) m_Lines[index].enabled = false;
            if (!impact) return;

            if (s.endsOnCharacter)
            {
                GameObject prefab = s.v.characterImpact != null ? s.v.characterImpact : s.v.surfaceImpact;
                ConquestFxPool.Play(prefab, s.endPoint, Quaternion.LookRotation(-s.v.dir), s.v.impactSeconds);
            }
            else if (s.endsOnSurface)
            {
                Vector3 normal = s.endNormal.sqrMagnitude > 1e-6f ? s.endNormal : -s.v.dir;
                ConquestFxPool.Play(s.v.surfaceImpact, s.endPoint, Quaternion.LookRotation(normal), s.v.impactSeconds);
            }
        }
    }
}
