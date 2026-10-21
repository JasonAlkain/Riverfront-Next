using UnityEngine;
using UnityEngine.UI;
using Game.Conquest.Settings;
using Game.Conquest.UI;

namespace Game.Conquest
{
    /// <summary>
    /// Takes its Graphic's colour from the UI theme by role, and follows the high-contrast option.
    /// TMP text gets the theme font as well.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public class ThemedGraphic : MonoBehaviour
    {
        public ConquestUITheme theme;
        public UIColorRole role = UIColorRole.Panel;

        private ConquestSettingsManager m_Subscribed;

        // Start as well as OnEnable: a menu opened directly creates the settings manager in another
        // object's Awake, which may run after this OnEnable.
        private void Start()
        {
            if (m_Subscribed == null) Subscribe();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void Subscribe()
        {
            if (m_Subscribed != null) m_Subscribed.OnSettingsChanged -= Apply;
            m_Subscribed = ConquestSettingsManager.Instance;
            if (m_Subscribed != null) m_Subscribed.OnSettingsChanged += Apply;
            Apply(m_Subscribed != null ? m_Subscribed.CurrentSettings : null);
        }

        private void OnDisable()
        {
            if (m_Subscribed != null) m_Subscribed.OnSettingsChanged -= Apply;
            m_Subscribed = null;
        }

        private void Apply(ConquestSettingsData settings)
        {
            if (theme == null) return;
            var graphic = GetComponent<Graphic>();
            graphic.color = theme.Get(role, settings != null && settings.highContrast);
            if (graphic is TMPro.TMP_Text text && theme.font != null && text.font != theme.font) text.font = theme.font;
        }
    }
}
