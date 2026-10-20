using UnityEngine;

namespace Game.Conquest.Combat
{
    /// <summary>
    /// Shot tracers drawn from a fixed ring of LineRenderers that is reused for the whole session,
    /// so sustained fire from many combatants allocates nothing. Tracers use shared material
    /// assets (never instanced). Purely visual: nothing here is networked.
    /// </summary>
    public class ConquestTracerPool : MonoBehaviour
    {
        // Enough for 32 v 32 automatic fire at short tracer lifetimes; the oldest is recycled when full.
        private const int Capacity = 96;

        private static ConquestTracerPool s_Instance;

        private readonly LineRenderer[] m_Lines = new LineRenderer[Capacity];
        private readonly float[] m_HideAt = new float[Capacity];
        private int m_Next;

        /// <summary>Draw a straight tracer from <paramref name="from"/> to <paramref name="to"/> for <paramref name="duration"/> seconds.</summary>
        public static void Show(Vector3 from, Vector3 to, Material material, float width, float duration)
        {
            if (material == null) return;
            if (s_Instance == null)
            {
                var root = new GameObject("ConquestTracerPool");
                DontDestroyOnLoad(root);
                s_Instance = root.AddComponent<ConquestTracerPool>();
            }
            s_Instance.ShowTracer(from, to, material, width, duration);
        }

        private void ShowTracer(Vector3 from, Vector3 to, Material material, float width, float duration)
        {
            int index = m_Next;
            m_Next = (m_Next + 1) % Capacity;

            LineRenderer line = m_Lines[index];
            if (line == null)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(transform, false);
                line = go.AddComponent<LineRenderer>();
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                m_Lines[index] = line;
            }

            line.sharedMaterial = material;
            line.startWidth = width;
            line.endWidth = width;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.enabled = true;
            m_HideAt[index] = Time.time + duration;
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = 0; i < Capacity; i++)
            {
                LineRenderer line = m_Lines[i];
                if (line != null && line.enabled && now >= m_HideAt[i]) line.enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
        }
    }
}
