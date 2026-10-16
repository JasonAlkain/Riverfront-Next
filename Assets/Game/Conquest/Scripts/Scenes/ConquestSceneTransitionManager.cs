using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Game.Conquest.Maps;

namespace Game.Conquest.Scenes
{
    public class ConquestSceneTransitionManager : MonoBehaviour
    {
        public static ConquestSceneTransitionManager Instance { get; private set; }

        [Header("Registry")]
        [SerializeField] private ConquestMapRegistry mapRegistry;

        [Header("Loading Tips")]
        [SerializeField] private string[] gameplayTips = new string[]
        {
            "Command Posts serve as reinforcement spawn points and contest zones.",
            "Holding a majority of Command Posts slowly bleeds enemy reinforcement tickets.",
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

        // Canvas & TMP UI References
        private Canvas m_LoadingCanvas;
        private CanvasGroup m_LoadingCanvasGroup;
        private TextMeshProUGUI m_MapNameText;
        private TextMeshProUGUI m_TipText;

        public bool IsTransitioning => m_IsTransitioning;

        public void SetMapRegistry(ConquestMapRegistry reg)
        {
            mapRegistry = reg;
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

            CreateLoadingCanvas();
        }

        private void CreateLoadingCanvas()
        {
            if (m_LoadingCanvas != null) return;

            var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (font == null)
            {
                font = TMP_Settings.defaultFontAsset;
            }

            GameObject canvasGO = new GameObject("LoadingScreenCanvas");
            canvasGO.transform.SetParent(transform, false);
            m_LoadingCanvas = canvasGO.AddComponent<Canvas>();
            m_LoadingCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_LoadingCanvas.sortingOrder = 999;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();
            m_LoadingCanvasGroup = canvasGO.AddComponent<CanvasGroup>();
            m_LoadingCanvasGroup.alpha = 0f;
            m_LoadingCanvasGroup.blocksRaycasts = false;

            // Fullscreen backdrop
            GameObject bgGO = new GameObject("Backdrop");
            bgGO.transform.SetParent(canvasGO.transform, false);
            var bgImg = bgGO.AddComponent<UnityEngine.UI.Image>();
            bgImg.color = new Color(0.04f, 0.06f, 0.09f, 0.98f);
            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;

            // Center Panel
            GameObject panelGO = new GameObject("CenterPanel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panelImg = panelGO.AddComponent<UnityEngine.UI.Image>();
            panelImg.color = new Color(0.08f, 0.12f, 0.18f, 0.92f);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(700, 320);

            // Title
            GameObject titleGO = new GameObject("TitleText");
            titleGO.transform.SetParent(panelGO.transform, false);
            var titleTMP = titleGO.AddComponent<TextMeshProUGUI>();
            titleTMP.font = font;
            titleTMP.text = "DEPLOYING TO BATTLEFIELD";
            titleTMP.fontSize = 32;
            titleTMP.fontStyle = FontStyles.Bold;
            titleTMP.color = new Color(0.35f, 0.78f, 1.0f);
            titleTMP.alignment = TextAlignmentOptions.Center;
            var titleRect = titleGO.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.7f);
            titleRect.anchorMax = new Vector2(1f, 0.95f);
            titleRect.sizeDelta = Vector2.zero;

            // Map Name Subtitle
            GameObject mapGO = new GameObject("MapNameText");
            mapGO.transform.SetParent(panelGO.transform, false);
            m_MapNameText = mapGO.AddComponent<TextMeshProUGUI>();
            m_MapNameText.font = font;
            m_MapNameText.fontSize = 24;
            m_MapNameText.fontStyle = FontStyles.Bold;
            m_MapNameText.color = new Color(1.0f, 0.75f, 0.2f);
            m_MapNameText.alignment = TextAlignmentOptions.Center;
            var mapRect = mapGO.GetComponent<RectTransform>();
            mapRect.anchorMin = new Vector2(0f, 0.5f);
            mapRect.anchorMax = new Vector2(1f, 0.72f);
            mapRect.sizeDelta = Vector2.zero;

            // Tactical Tip Box
            GameObject tipBoxGO = new GameObject("TipBox");
            tipBoxGO.transform.SetParent(panelGO.transform, false);
            var tipBoxImg = tipBoxGO.AddComponent<UnityEngine.UI.Image>();
            tipBoxImg.color = new Color(0.12f, 0.16f, 0.24f, 0.9f);
            var tipBoxRect = tipBoxGO.GetComponent<RectTransform>();
            tipBoxRect.anchorMin = new Vector2(0.05f, 0.08f);
            tipBoxRect.anchorMax = new Vector2(0.95f, 0.48f);
            tipBoxRect.sizeDelta = Vector2.zero;

            GameObject tipGO = new GameObject("TipText");
            tipGO.transform.SetParent(tipBoxGO.transform, false);
            m_TipText = tipGO.AddComponent<TextMeshProUGUI>();
            m_TipText.font = font;
            m_TipText.fontSize = 17;
            m_TipText.color = Color.white;
            m_TipText.alignment = TextAlignmentOptions.Center;
            m_TipText.textWrappingMode = TextWrappingModes.Normal;
            var tipRect = tipGO.GetComponent<RectTransform>();
            tipRect.anchorMin = new Vector2(0.04f, 0.05f);
            tipRect.anchorMax = new Vector2(0.96f, 0.95f);
            tipRect.sizeDelta = Vector2.zero;
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

        /// <summary>Abandon whatever is loading and go back to the main menu with a message.</summary>
        public void FailTransition(string message)
        {
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
            CreateLoadingCanvas();

            if (m_FadeCoroutine != null)
            {
                StopCoroutine(m_FadeCoroutine);
                m_FadeCoroutine = null;
            }

            if (mapRegistry != null)
            {
                var mapData = mapRegistry.GetMapBySceneName(sceneName);
                m_CurrentMapName = mapData != null ? mapData.displayName : sceneName;
            }
            else
            {
                m_CurrentMapName = sceneName;
            }

            if (gameplayTips != null && gameplayTips.Length > 0)
            {
                m_CurrentTip = gameplayTips[UnityEngine.Random.Range(0, gameplayTips.Length)];
            }

            if (m_MapNameText != null) m_MapNameText.text = $"[ {m_CurrentMapName.ToUpper()} ]";
            if (m_TipText != null) m_TipText.text = $"TACTICAL TIP:\n{m_CurrentTip}";

            if (m_LoadingCanvasGroup != null)
            {
                m_LoadingCanvasGroup.alpha = 1.0f;
                m_LoadingCanvasGroup.blocksRaycasts = true;
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
            if (m_LoadingCanvasGroup != null)
            {
                while (m_LoadingCanvasGroup.alpha > 0f)
                {
                    m_LoadingCanvasGroup.alpha -= Time.unscaledDeltaTime * 3.0f;
                    yield return null;
                }
                m_LoadingCanvasGroup.alpha = 0f;
                m_LoadingCanvasGroup.blocksRaycasts = false;
            }
            m_IsTransitioning = false;
            m_FadeCoroutine = null;
        }
    }
}

