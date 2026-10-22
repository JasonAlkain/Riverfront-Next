using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>
    /// Four lines around screen centre that open to the weapon's spread cone, plus a hitmarker
    /// (an X of four lines) that flashes on a hit and shows in the kill colour on a kill.
    /// </summary>
    public class CrosshairUI : MonoBehaviour
    {
        [Header("Lines (moved outward along their own axis)")]
        public RectTransform line_Top;
        public RectTransform line_Bottom;
        public RectTransform line_Left;
        public RectTransform line_Right;
        public Image[] lineImages;

        [Header("Hitmarker")]
        public GameObject hitmarker;
        public Image[] hitmarkerImages;

        [Header("Look")]
        [Tooltip("Gap in canvas pixels between screen centre and each line when spread is zero.")]
        public float minGap = 6f;
        public float maxGap = 80f;
        public Color normalColor = new Color(1f, 1f, 1f, 0.85f);
        public Color hitColor = Color.white;
        public Color killColor = new Color(1f, 0.3f, 0.2f, 1f);

        private float m_Gap = -1f;
        private int m_HitState = -1; // 0 none, 1 hit, 2 kill
        private bool m_Hidden;

        /// <param name="spreadGap">Extra gap in canvas pixels for the current spread.</param>
        public void SetSpread(float spreadGap)
        {
            float gap = Mathf.Clamp(minGap + spreadGap, minGap, maxGap);
            if (Mathf.Abs(gap - m_Gap) < 0.25f) return;
            m_Gap = gap;
            Place(line_Top, new Vector2(0f, gap));
            Place(line_Bottom, new Vector2(0f, -gap));
            Place(line_Left, new Vector2(-gap, 0f));
            Place(line_Right, new Vector2(gap, 0f));
        }

        public void SetHitmarker(bool showHit, bool kill = false)
        {
            int state = !showHit ? 0 : (kill ? 2 : 1);
            if (state == m_HitState) return;
            m_HitState = state;

            if (hitmarker != null && hitmarker.activeSelf != showHit) hitmarker.SetActive(showHit);
            Color c = kill ? killColor : hitColor;
            if (hitmarkerImages != null)
            {
                for (int i = 0; i < hitmarkerImages.Length; i++)
                {
                    if (hitmarkerImages[i] != null) hitmarkerImages[i].color = c;
                }
            }
        }

        /// <summary>Hide only the four lines (a scoped view draws its own reticle) but keep hitmarkers.</summary>
        public void SetLinesHidden(bool hidden)
        {
            if (hidden == m_Hidden) return;
            m_Hidden = hidden;
            if (lineImages == null) return;
            for (int i = 0; i < lineImages.Length; i++)
            {
                if (lineImages[i] != null) lineImages[i].color = hidden ? Color.clear : normalColor;
            }
        }

        private static void Place(RectTransform line, Vector2 position)
        {
            if (line != null) line.anchoredPosition = position;
        }

        // Show and hide the crosshair
        public void Show() { if (!gameObject.activeSelf) gameObject.SetActive(true); }
        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }
    }
}
