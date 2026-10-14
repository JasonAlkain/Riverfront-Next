using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.Conquest.Settings;
using Game.Conquest.Networking;

namespace Game.Conquest.Scenes
{
    public class ConquestBootstrap : MonoBehaviour
    {
        [Header("Startup Configuration")]
        [SerializeField] private string targetMenuScene = "MainMenu";
        [SerializeField] private float minimumSplashDuration = 0.5f;

        private float m_Timer = 0f;
        private bool m_HasLoaded = false;

        private void Start()
        {
            Debug.Log("[ConquestBootstrap] Initializing Conquest systems...");

            // Initialize settings
            if (ConquestSettingsManager.Instance != null)
            {
                ConquestSettingsManager.Instance.LoadSettings();
                Debug.Log("[ConquestBootstrap] Settings initialized.");
            }
        }

        private void Update()
        {
            if (m_HasLoaded) return;

            m_Timer += Time.unscaledDeltaTime;
            if (m_Timer >= minimumSplashDuration)
            {
                m_HasLoaded = true;
                Debug.Log($"[ConquestBootstrap] Loading {targetMenuScene}...");
                UnityEngine.SceneManagement.SceneManager.LoadScene(targetMenuScene);
            }
        }
    }
}
