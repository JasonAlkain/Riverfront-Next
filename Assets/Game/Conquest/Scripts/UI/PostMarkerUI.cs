using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>In-world marker over a command post: owner colour, name, distance and capture progress.</summary>
    public class PostMarkerUI : MonoBehaviour
    {
        [Header("Images")]
        public Image img_Icon;
        [Tooltip("Image Type Filled (Radial 360): fillAmount is the capture progress.")]
        public Image img_Progress;

        [Header("Text Elements")]
        public TextMeshProUGUI txt_Label;
        public TextMeshProUGUI txt_Distance;

        private string m_Label;
        private int m_Meters = -1;
        private Color m_Color;
        private float m_Progress = -1f;

        public RectTransform Rect => (RectTransform)transform;

        public void SetPost(string label, Color color, Sprite ownerIcon, int meters, float progress, Color progressColor)
        {
            if (img_Icon != null && ownerIcon != null && img_Icon.sprite != ownerIcon) img_Icon.sprite = ownerIcon;
            if (label != m_Label)
            {
                m_Label = label;
                if (txt_Label != null) txt_Label.text = label;
            }
            if (meters != m_Meters)
            {
                m_Meters = meters;
                if (txt_Distance != null) txt_Distance.SetText("{0} m", (float)meters);
            }
            if (color != m_Color)
            {
                m_Color = color;
                if (img_Icon != null) img_Icon.color = color;
            }
            if (img_Progress != null)
            {
                bool show = progress > 0f && progress < 1f;
                if (img_Progress.enabled != show) img_Progress.enabled = show;
                if (show && Mathf.Abs(progress - m_Progress) > 0.005f)
                {
                    m_Progress = progress;
                    img_Progress.fillAmount = progress;
                    img_Progress.color = progressColor;
                }
            }
        }
    }
}
