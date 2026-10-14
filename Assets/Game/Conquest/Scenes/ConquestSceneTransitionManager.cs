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

        private bool m_IsTransitioning = false;
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
        private bool m_NetworkEventsSubscribed = false;

        private void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += HandleUnitySceneLoaded;
        }

        private void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= HandleUnitySceneLoaded;
            UnsubscribeNetworkEvents();
        }

        private void Update()
        {
            if (!m_NetworkEventsSubscribed && NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            {
                SubscribeNetworkEvents();
            }
        }

        private void SubscribeNetworkEvents()
        {
            if (m_NetworkEventsSubscribed) return;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnSceneEvent += HandleSceneEvent;
                m_NetworkEventsSubscribed = true;
            }
        }

        private void UnsubscribeNetworkEvents()
        {
            if (!m_NetworkEventsSubscribed) return;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnSceneEvent -= HandleSceneEvent;
            }
            m_NetworkEventsSubscribed = false;
        }

        private void HandleUnitySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // If we loaded into any gameplay map or non-bootstrap scene, ensure loading screen hides
            if (scene.name != "Bootstrap" && scene.name != "MainMenu")
            {
                StartCoroutine(DelayedHideLoading());
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
            m_IsTransitioning = true;
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
            m_IsTransitioning = true;

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

