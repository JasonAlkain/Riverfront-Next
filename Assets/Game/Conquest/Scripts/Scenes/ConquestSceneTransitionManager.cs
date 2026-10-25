using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.Conquest.Maps;

namespace Game.Conquest.Scenes
{
    public class ConquestSceneTransitionManager : MonoBehaviour
    {
        public static ConquestSceneTransitionManager Instance { get; private set; }

        [Header("Registry")]
        [SerializeField] private ConquestMapRegistry mapRegistry;

        [Header("Loading Screen")]
        [SerializeField] private LoadingScreenUI loadingScreenPrefab;

        [Header("Loading Tips")]
        [SerializeField] private string[] gameplayTips = new string[]
        {
            "Command Posts serve as reinforcement spawn points and contest zones.",
            "Every respawn costs your team a reinforcement ticket. Out of tickets and out of soldiers, you lose.",
            "Heavies excel at suppression, while Recons pick off high-value targets from afar.",
            "Engineers can deploy support stations with [Q] to heal and resupply nearby squadmates.",
            "Press [R] to reload before entering a contested capture point.",
            "Bots continuously push towards enemy and neutral command posts unless engaging hostiles.",
            "Stick with your infantry squad to secure command posts faster."
        };

        [Header("Failure Handling")]
        [Tooltip("If a load hasn't finished (including network sync) after this long, give up and return to the menu.")]
        [SerializeField] private float loadTimeoutSeconds = 60f;

        private bool m_IsTransitioning = false;
        private float m_TransitionStartTime;
        private NetworkManager m_DisconnectSubscribedManager;
        private string m_TargetScene = "";
        private string m_CurrentMapName = "";
        private string m_CurrentTip = "";

        private LoadingScreenUI m_LoadingScreen;

        public bool IsTransitioning => m_IsTransitioning;

        public void SetMapRegistry(ConquestMapRegistry reg)
        {
            mapRegistry = reg;
        }

        public void SetLoadingScreenPrefab(LoadingScreenUI prefab)
        {
            if (loadingScreenPrefab == null) loadingScreenPrefab = prefab;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // The loading screen is a prefab instance under this persistent object, made on first use so
        // a manager added by the main menu's fallback can still be handed the prefab first.
        private void EnsureLoadingScreen()
        {
            if (m_LoadingScreen != null || loadingScreenPrefab == null) return;
            m_LoadingScreen = Instantiate(loadingScreenPrefab, transform);
            m_LoadingScreen.Alpha = 0f;
        }

        private Coroutine m_FadeCoroutine;
        // NetworkManager creates a new NetworkSceneManager on every start and disposes it on
        // shutdown, so track the instance rather than a bool or later sessions miss scene events.
        private NetworkSceneManager m_SubscribedSceneManager;

        private void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += HandleUnitySceneLoaded;
        }

        private void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= HandleUnitySceneLoaded;
            UnsubscribeNetworkEvents();
            if (m_DisconnectSubscribedManager != null)
            {
                m_DisconnectSubscribedManager.OnClientDisconnectCallback -= HandleClientDisconnect;
                m_DisconnectSubscribedManager = null;
            }
        }

        private void Update()
        {
            var currentSceneManager = NetworkManager.Singleton != null ? NetworkManager.Singleton.SceneManager : null;
            if (currentSceneManager != m_SubscribedSceneManager)
            {
                SubscribeNetworkEvents();
            }

            var nm = NetworkManager.Singleton;
            if (nm != m_DisconnectSubscribedManager)
            {
                if (m_DisconnectSubscribedManager != null) m_DisconnectSubscribedManager.OnClientDisconnectCallback -= HandleClientDisconnect;
                if (nm != null) nm.OnClientDisconnectCallback += HandleClientDisconnect;
                m_DisconnectSubscribedManager = nm;
            }

            // A missed load/sync event used to latch m_IsTransitioning and soft-lock the menu.
            if (m_IsTransitioning && Time.unscaledTime - m_TransitionStartTime > loadTimeoutSeconds)
            {
                FailTransition("Timed out loading the battlefield.");
            }
        }

        private void BeginTransition()
        {
            m_IsTransitioning = true;
            m_TransitionStartTime = Time.unscaledTime;
        }

        private static bool IsMenuScene(string sceneName)
        {
            return sceneName == "MainMenu" || sceneName == "Bootstrap";
        }

        // Fires on a client for its own disconnect (server shut down, kicked, lost connection).
        private void HandleClientDisconnect(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.IsServer) return;

            bool inMatch = !IsMenuScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            if (!m_IsTransitioning && !inMatch) return; // the menu handles lobby/connect failures itself

            string reason = nm.DisconnectReason;
            // Defer a frame: we're inside NGO's disconnect callback and FailTransition shuts it down.
            StartCoroutine(FailNextFrame(string.IsNullOrEmpty(reason) ? "Lost connection to the server." : reason));
        }

        private IEnumerator FailNextFrame(string message)
        {
            yield return null;
            FailTransition(message);
        }

        /// <summary>A failure to show in a dialog once the menu is back; taken (and cleared) by the menu.</summary>
        public static string PendingMenuMessage { get; private set; }

        public static string TakePendingMenuMessage()
        {
            string m = PendingMenuMessage;
            PendingMenuMessage = null;
            return m;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPendingMessage() => PendingMenuMessage = null;

        /// <summary>Abandon whatever is loading and go back to the main menu with a message.</summary>
        public void FailTransition(string message)
        {
            PendingMenuMessage = message;
            Debug.LogWarning($"[ConquestSceneTransitionManager] {message}");
            StopAllCoroutines();
            m_FadeCoroutine = null;
            m_IsTransitioning = false;

            Game.Conquest.Networking.ConquestSessionManager.Instance?.ClearPlayerAssignments();
            Game.Conquest.Networking.ConquestSessionManager.Instance?.SetStatus(message);

            // Always reload the menu: an async map load started by NGO can still finish after the
            // connection is gone, and queuing MainMenu after it guarantees we end up back there.
            ReturnToMainMenu();
        }

        private void SubscribeNetworkEvents()
        {
            var current = NetworkManager.Singleton != null ? NetworkManager.Singleton.SceneManager : null;
            if (current == m_SubscribedSceneManager) return;

            UnsubscribeNetworkEvents();
            if (current != null)
            {
                current.OnSceneEvent += HandleSceneEvent;
                m_SubscribedSceneManager = current;
            }
        }

        private void UnsubscribeNetworkEvents()
        {
            if (m_SubscribedSceneManager != null)
            {
                m_SubscribedSceneManager.OnSceneEvent -= HandleSceneEvent;
            }
            m_SubscribedSceneManager = null;
        }

        private void HandleUnitySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Gameplay maps in a network session hide the overlay only once this peer is synchronised
            // (scene events / ConquestMatchManager.OnNetworkSpawn); a Unity load alone isn't enough.
            if (!IsMenuScene(scene.name))
            {
                bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
                if (!networked) StartCoroutine(DelayedHideLoading());
            }
            else if (scene.name == "MainMenu" && m_IsTransitioning)
            {
                StartCoroutine(DelayedHideLoading());
            }
        }

        private IEnumerator DelayedHideLoading()
        {
            yield return new WaitForSecondsRealtime(0.2f);
            HideLoading();
        }

        private void HandleSceneEvent(SceneEvent sceneEvent)
        {
            switch (sceneEvent.SceneEventType)
            {
                case SceneEventType.Load:
                    // Clients only learn about a server-driven map load here, so start the
                    // transition (and its timeout) now; otherwise a failure mid-load leaves the overlay up.
                    if (!m_IsTransitioning) BeginTransition();
                    ShowLoading(sceneEvent.SceneName);
                    break;
                case SceneEventType.LoadComplete:
                    // If local client or server finished loading
                    if (NetworkManager.Singleton != null &&
                        (sceneEvent.ClientId == NetworkManager.Singleton.LocalClientId || NetworkManager.Singleton.IsServer))
                    {
                        HideLoading();
                    }
                    break;
                case SceneEventType.LoadEventCompleted:
                case SceneEventType.SynchronizeComplete:
                    HideLoading();
                    break;
            }
        }

        public void TransitionToMap(string sceneName, bool isNetworked = true)
        {
            if (m_IsTransitioning) return;
            BeginTransition();
            m_TargetScene = sceneName;

            ShowLoading(sceneName);
            SubscribeNetworkEvents();

            if (isNetworked && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            }
            else
            {
                StartCoroutine(LocalLoadCoroutine(sceneName));
            }
        }

        public void ReturnToMainMenu()
        {
            if (m_IsTransitioning) return;
            BeginTransition();

            ShowLoading("MainMenu");

            if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
            {
                NetworkManager.Singleton.Shutdown();
            }

            StartCoroutine(LocalLoadCoroutine("MainMenu"));
        }

        private IEnumerator LocalLoadCoroutine(string sceneName)
        {
            yield return new WaitForSecondsRealtime(0.3f);

            AsyncOperation asyncOp = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName);
            while (asyncOp != null && !asyncOp.isDone)
            {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(0.2f);
            HideLoading();
            m_IsTransitioning = false;
        }

        public void ShowLoading(string sceneName)
        {
            EnsureLoadingScreen();

            if (m_FadeCoroutine != null)
            {
                StopCoroutine(m_FadeCoroutine);
                m_FadeCoroutine = null;
            }

            // GetMapBySceneName falls back to the default map, so only trust an exact match;
            // non-map scenes (results, lobby) get a plain label instead of a wrong map name.
            var mapData = mapRegistry != null ? mapRegistry.GetMapBySceneName(sceneName) : null;
            if (mapData != null && mapData.sceneName == sceneName)
                m_CurrentMapName = mapData.displayName;
            else if (sceneName == "MainMenu")
                m_CurrentMapName = "Lobby";
            else if (sceneName == "MatchResults")
                m_CurrentMapName = "Match Results";
            else
                m_CurrentMapName = sceneName;

            if (gameplayTips != null && gameplayTips.Length > 0)
            {
                m_CurrentTip = gameplayTips[UnityEngine.Random.Range(0, gameplayTips.Length)];
            }

            if (m_LoadingScreen != null)
            {
                m_LoadingScreen.SetContent(m_CurrentMapName, m_CurrentTip);
                m_LoadingScreen.Alpha = 1f;
            }
        }

        public void HideLoading()
        {
            if (m_FadeCoroutine != null)
            {
                StopCoroutine(m_FadeCoroutine);
            }
            m_FadeCoroutine = StartCoroutine(FadeOutLoading());
        }

        private IEnumerator FadeOutLoading()
        {
            if (m_LoadingScreen != null)
            {
                // Reduce motion: no fade, the screen just goes.
                bool instant = Game.Conquest.Settings.ConquestSettingsManager.Instance != null &&
                               Game.Conquest.Settings.ConquestSettingsManager.Instance.CurrentSettings.reduceMotion;
                while (!instant && m_LoadingScreen.Alpha > 0f)
                {
                    m_LoadingScreen.Alpha = Mathf.Max(0f, m_LoadingScreen.Alpha - Time.unscaledDeltaTime * 3.0f);
                    yield return null;
                }
                m_LoadingScreen.Alpha = 0f;
            }
            m_IsTransitioning = false;
            m_FadeCoroutine = null;
        }
    }
}

