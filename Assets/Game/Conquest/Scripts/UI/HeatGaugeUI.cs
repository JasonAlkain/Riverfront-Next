using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Game.Conquest.Settings;
using Game.Conquest.UI;

namespace Game.Conquest
{
    /// <summary>
    /// Weapon heat for teams whose weapons overheat: a bar that fills as you fire and the percent,
    /// or OVERHEATED while the weapon is locked. Colour is never the only cue: the text carries
    /// the state too. Only touches the UI when the value changes, so it can be set every frame.
    /// </summary>
    public class HeatGaugeUI : MonoBehaviour
    {
        [Header("Elements")]
        [Tooltip("Image Type Filled (Horizontal): fillAmount is the heat.")]
        public Image img_Fill;
        public TextMeshProUGUI txt_Heat;

        [Header("Theme")]
        public ConquestUITheme theme;

        [Header("Look")]
        [Tooltip("Pulses per second of the fill while overheated (off with reduce motion).")]
        public float overheatPulseRate = 3f;

        private int m_Percent = -1;
        private bool m_Overheated;
        private Color m_FillColor;

        public void SetHeat(int percent, bool overheated)
        {
            percent = Mathf.Clamp(percent, 0, 100);
            if (percent == m_Percent && overheated == m_Overheated) return;
            m_Percent = percent;
            m_Overheated = overheated;

            if (img_Fill != null)
            {
                img_Fill.fillAmount = percent / 100f;
                m_FillColor = theme == null ? Color.white : (overheated ? theme.overheated : theme.HeatColor(percent / 100f));
                img_Fill.color = m_FillColor;
            }
            if (txt_Heat != null)
            {
                if (overheated) txt_Heat.SetText("OVERHEATED");
                else txt_Heat.SetText("HEAT {0}%", (float)percent);
            }
        }

        private void Update()
        {
            if (!m_Overheated || img_Fill == null || ConquestSettingsManager.ReduceMotion || overheatPulseRate <= 0f) return;
            Color c = m_FillColor;
            c.a *= 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * overheatPulseRate * Mathf.PI));
            img_Fill.color = c;
        }

        // Show and hide the gauge
        public void Show() { if (!gameObject.activeSelf) gameObject.SetActive(true); }
        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }
    }
}
