using UnityEngine;
using UnityEngine.UI;
using Game.Conquest.Settings;

namespace Game.Conquest
{
    /// <summary>
    /// Applies the UI scale setting to a canvas: a smaller reference resolution makes everything
    /// on a scale-with-screen-size canvas bigger. Put it next to the CanvasScaler.
    /// </summary>
    [RequireComponent(typeof(CanvasScaler))]
    public class CanvasUIScale : MonoBehaviour
    {
        private CanvasScaler m_Scaler;
        private Vector2 m_BaseResolution;
        private ConquestSettingsManager m_Subscribed;

        private void Awake()
        {
            m_Scaler = GetComponent<CanvasScaler>();
            m_BaseResolution = m_Scaler.referenceResolution;
        }

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
            if (m_Subscribed == null) return;
            m_Subscribed.OnSettingsChanged += Apply;
            Apply(m_Subscribed.CurrentSettings);
        }

        private void OnDisable()
        {
            if (m_Subscribed != null) m_Subscribed.OnSettingsChanged -= Apply;
            m_Subscribed = null;
        }

        private void Apply(ConquestSettingsData settings)
        {
            float scale = settings != null ? Mathf.Clamp(settings.uiScale, 0.5f, 2f) : 1f;
            m_Scaler.referenceResolution = m_BaseResolution / scale;
        }
    }
}
