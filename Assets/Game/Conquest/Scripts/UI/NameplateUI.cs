using TMPro;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>Name and health bar above a nearby teammate.</summary>
    public class NameplateUI : MonoBehaviour
    {
        [Header("Text Elements")]
        public TextMeshProUGUI txt_Name;

        [Header("Images")]
        [Tooltip("Image Type Filled (Horizontal): fillAmount is the health fraction.")]
        public Image img_HealthFill;

        [Header("Group")]
        public CanvasGroup canvasGroup;

        private FixedString64Bytes m_Name;
        private bool m_HasName;
        private float m_Health = -1f;

        public RectTransform Rect => (RectTransform)transform;

        // Takes the replicated fixed string so the name is only turned into a managed string when it changes.
        public void SetPlayer(in FixedString64Bytes playerName, float healthFraction, Color color, float alpha)
        {
            if (!m_HasName || playerName != m_Name)
            {
                m_Name = playerName;
                m_HasName = true;
                if (txt_Name != null) txt_Name.text = playerName.ToString();
            }
            if (txt_Name != null && txt_Name.color != color) txt_Name.color = color;
            if (img_HealthFill != null && Mathf.Abs(healthFraction - m_Health) > 0.005f)
            {
                m_Health = healthFraction;
                img_HealthFill.fillAmount = healthFraction;
            }
            if (canvasGroup != null) canvasGroup.alpha = alpha;
        }
    }
}
