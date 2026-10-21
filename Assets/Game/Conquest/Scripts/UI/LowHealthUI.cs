using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>
    /// Red screen-edge vignette below a health threshold. It gets stronger as health drops and
    /// pulses gently unless reduce motion is on.
    /// </summary>
    public class LowHealthUI : MonoBehaviour
    {
        [Header("Images")]
        public Image img_Vignette;

        [Header("Look")]
        [Tooltip("Placeholder: health fraction at or below which the vignette shows.")]
        [Range(0f, 1f)] public float threshold = 0.35f;
        public Color vignetteColor = new Color(0.75f, 0.05f, 0.05f, 0.6f);
        [Tooltip("Pulses per second near death. Zero turns pulsing off.")]
        public float pulseRate = 1.5f;

        public bool ReduceMotion { get; set; }

        /// <summary>Health fraction of the local player, or a negative value to hide.</summary>
        public void Tick(float healthFraction)
        {
            if (img_Vignette == null) return;

            bool show = healthFraction >= 0f && healthFraction <= threshold && threshold > 0f;
            if (img_Vignette.enabled != show) img_Vignette.enabled = show;
            if (!show) return;

            // 0 at the threshold, 1 at zero health.
            float severity = 1f - Mathf.Clamp01(healthFraction / threshold);
            float pulse = ReduceMotion || pulseRate <= 0f ? 1f : 0.8f + 0.2f * Mathf.Sin(Time.time * pulseRate * Mathf.PI * 2f);
            Color c = vignetteColor;
            c.a *= Mathf.Lerp(0.35f, 1f, severity) * pulse;
            img_Vignette.color = c;
        }
    }
}
