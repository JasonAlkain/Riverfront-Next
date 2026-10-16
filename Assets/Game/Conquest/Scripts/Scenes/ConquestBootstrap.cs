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

        private void BeginDedicatedServer()
        {
            var args = ConquestServerLaunchArgs.Parse(System.Environment.GetCommandLineArgs());
            Debug.Log("[ConquestBootstrap] Dedicated server launch detected.");

            // A headless server has nothing to render; don't spin the CPU on frames.
            Application.targetFrameRate = 60;

            // The lobby NetworkObject and NetworkManager live in the menu scene, so load it first and
            // start the server once it's active. The persistent session manager outlives this object.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnMenuLoadedForServer;
            s_ServerArgs = args;
            UnityEngine.SceneManagement.SceneManager.LoadScene(targetMenuScene);
        }

        private static ConquestServerLaunchArgs s_ServerArgs;

        private static void OnMenuLoadedForServer(Scene scene, LoadSceneMode mode)
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnMenuLoadedForServer;
            var session = ConquestSessionManager.Instance;
            if (session == null)
            {
                Debug.LogError("[ConquestBootstrap] Dedicated server: no ConquestSessionManager; quitting.");
                Application.Quit(1);
                return;
            }
            session.StartDedicatedServerDeferred(s_ServerArgs);
        }

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

                if (ConquestServerLaunchArgs.IsDedicatedServerLaunch())
                {
                    BeginDedicatedServer();
                    return;
                }

                Debug.Log($"[ConquestBootstrap] Loading {targetMenuScene}...");
                UnityEngine.SceneManagement.SceneManager.LoadScene(targetMenuScene);
            }
        }
    }
}
