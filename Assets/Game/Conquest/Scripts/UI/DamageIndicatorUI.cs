using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>
    /// Arrows around the crosshair pointing at whoever just hurt the local player. Each slot is a
    /// pivot at screen centre with its arrow image offset outward; the pivot turns to face the
    /// attacker and the arrow fades out. The newest hit reuses the oldest slot.
    /// </summary>
    public class DamageIndicatorUI : MonoBehaviour
    {
        [Header("Slots (pivot at screen centre, arrow child offset upward)")]
        public RectTransform[] pivots;
        public Image[] arrows;

        [Header("Look")]
        public float showSeconds = 1.2f;
        public Color arrowColor = new Color(1f, 0.15f, 0.1f, 0.85f);

        private Vector3[] m_Sources;
        private float[] m_EndTimes;
        private int m_Next;

        private void Awake()
        {
            int count = pivots != null ? pivots.Length : 0;
            m_Sources = new Vector3[count];
            m_EndTimes = new float[count];
            for (int i = 0; i < count; i++) SetSlotVisible(i, false);
        }

        /// <summary>Point an arrow at a world position the damage came from.</summary>
        public void ShowFrom(Vector3 worldSource)
        {
            if (m_Sources == null || m_Sources.Length == 0) return;
            int slot = m_Next;
            m_Next = (m_Next + 1) % m_Sources.Length;
            m_Sources[slot] = worldSource;
            m_EndTimes[slot] = Time.time + showSeconds;
        }

        /// <summary>Turn and fade the arrows for the viewer's current position and facing. Pass null to hide them all.</summary>
        public void Tick(Transform viewer)
        {
            if (m_Sources == null) return;
            float now = Time.time;
            for (int i = 0; i < m_Sources.Length; i++)
            {
                float left = m_EndTimes[i] - now;
                if (viewer == null || left <= 0f)
                {
                    SetSlotVisible(i, false);
                    continue;
                }

                // Flatten to the ground plane: 0 degrees is straight ahead, positive is to the right.
                Vector3 toSource = m_Sources[i] - viewer.position;
                Vector3 local = viewer.InverseTransformDirection(new Vector3(toSource.x, 0f, toSource.z));
                float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                if (pivots[i] != null) pivots[i].localRotation = Quaternion.Euler(0f, 0f, -angle);

                SetSlotVisible(i, true);
                if (arrows != null && i < arrows.Length && arrows[i] != null)
                {
                    Color c = arrowColor;
                    c.a *= Mathf.Clamp01(left / showSeconds);
                    arrows[i].color = c;
                }
            }
        }

        private void SetSlotVisible(int i, bool visible)
        {
            if (pivots == null || i >= pivots.Length || pivots[i] == null) return;
            if (pivots[i].gameObject.activeSelf != visible) pivots[i].gameObject.SetActive(visible);
        }
    }
}
