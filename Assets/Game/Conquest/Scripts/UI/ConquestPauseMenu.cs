using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using Game.Conquest.Player;
using Game.Conquest.Settings;
using Game.Conquest.Networking;
using Game.Conquest.Scenes;
using Game.Conquest.Gameplay;

namespace Game.Conquest.UI
{
    public class ConquestPauseMenu : MonoBehaviour
    {
        public static ConquestPauseMenu Instance { get; private set; }

        private bool m_IsPaused = false;
        private ConquestPlayer m_LocalPlayer;

        // Canvas & TMP References
        private Canvas m_PauseCanvas;
        private CanvasScaler m_CanvasScaler;
        private TMP_FontAsset m_Font;

        // Sub-panels
        private GameObject m_MainPausePanel;
        private GameObject m_SettingsPanel;
        private GameObject m_LeaveConfirmPanel;
        private GameObject m_MatchEndNavPanel;

        // Settings Sliders & Labels
        private Slider m_MasterSlider;
        private Slider m_SfxSlider;
        private Slider m_SensitivitySlider;
        private Slider m_FovSlider;
        private Toggle m_HighContrastToggle;

        private TextMeshProUGUI m_MasterLabel;
        private TextMeshProUGUI m_SfxLabel;
        private TextMeshProUGUI m_SensitivityLabel;
        private TextMeshProUGUI m_FovLabel;

        public bool IsPaused => m_IsPaused;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            BuildPauseCanvas();
        }

        private void Start()
        {
            FindLocalPlayer();
        }

        private void FindLocalPlayer()
        {
            if (m_LocalPlayer != null) return;
            foreach (var p in FindObjectsByType<ConquestPlayer>(FindObjectsInactive.Exclude))
            {
                if (p.IsOwner && !p.isBot)
                {
                    m_LocalPlayer = p;
                    break;
                }
            }
        }

        private void BuildPauseCanvas()
        {
            m_Font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (m_Font == null) m_Font = TMP_Settings.defaultFontAsset;

            GameObject canvasGO = new GameObject("PauseMenuCanvas");
            canvasGO.transform.SetParent(transform, false);

            m_PauseCanvas = canvasGO.AddComponent<Canvas>();
            m_PauseCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_PauseCanvas.sortingOrder = 200;

            m_CanvasScaler = canvasGO.AddComponent<CanvasScaler>();
            m_CanvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            m_CanvasScaler.referenceResolution = new Vector2(1920, 1080);
            m_CanvasScaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            // Fullscreen Dim Backdrop
            GameObject dimGO = new GameObject("DimBackdrop");
            dimGO.transform.SetParent(canvasGO.transform, false);
            var dimImg = dimGO.AddComponent<UnityEngine.UI.Image>();
            dimImg.color = new Color(0.04f, 0.06f, 0.09f, 0.85f);
            var dimRect = dimGO.GetComponent<RectTransform>();
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.sizeDelta = Vector2.zero;

            // 1. MAIN PAUSE PANEL
            BuildMainPanel(canvasGO);

            // 2. SETTINGS PANEL
            BuildSettingsPanel(canvasGO);

            // 3. LEAVE CONFIRM PANEL
            BuildLeaveConfirmPanel(canvasGO);

            // 4. MATCH END NAVIGATION BUTTON
            BuildMatchEndNav(canvasGO);

            m_PauseCanvas.enabled = false;
        }

        private void BuildMainPanel(GameObject canvasGO)
        {
            m_MainPausePanel = new GameObject("MainPausePanel");
            m_MainPausePanel.transform.SetParent(canvasGO.transform, false);
            var panelRect = m_MainPausePanel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(380, 420);

            var bg = m_MainPausePanel.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color(0.08f, 0.11f, 0.16f, 0.98f);

            var vLayout = m_MainPausePanel.AddComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(30, 30, 25, 25);
            vLayout.spacing = 14;
            vLayout.childAlignment = TextAnchor.MiddleCenter;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = false;

            // Header Title
            CreateTMP(m_MainPausePanel, "TACTICAL PAUSE", 26, new Color(0.35f, 0.78f, 1.0f), TextAlignmentOptions.Center, FontStyles.Bold, 38);
            CreateTMP(m_MainPausePanel, "Simulation Active (Multiplayer Safe)", 13, Color.gray, TextAlignmentOptions.Center, FontStyles.Normal, 22);

            CreateButton(m_MainPausePanel, "RESUME GAME", new Color(0.2f, 0.6f, 1f), 48, () => SetPause(false));
            CreateButton(m_MainPausePanel, "SETTINGS", new Color(0.25f, 0.32f, 0.42f), 44, () => ShowSubPanel(m_SettingsPanel));
            CreateButton(m_MainPausePanel, "LEAVE MATCH", new Color(0.85f, 0.3f, 0.2f), 44, () => ShowSubPanel(m_LeaveConfirmPanel));
            CreateButton(m_MainPausePanel, "QUIT TO DESKTOP", new Color(0.25f, 0.30f, 0.38f), 42, QuitApplication);
        }

        private void BuildSettingsPanel(GameObject canvasGO)
        {
            m_SettingsPanel = new GameObject("SettingsPanel");
            m_SettingsPanel.transform.SetParent(canvasGO.transform, false);
            var panelRect = m_SettingsPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(560, 480);

            var bg = m_SettingsPanel.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color(0.08f, 0.11f, 0.16f, 0.98f);

            var vLayout = m_SettingsPanel.AddComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(35, 35, 25, 25);
            vLayout.spacing = 10;
            vLayout.childAlignment = TextAnchor.UpperCenter;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = false;

            CreateTMP(m_SettingsPanel, "GAME SETTINGS", 24, new Color(0.35f, 0.78f, 1.0f), TextAlignmentOptions.Center, FontStyles.Bold, 34);

            m_MasterLabel = CreateTMP(m_SettingsPanel, "Master Volume: 85%", 14, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, 20);
            m_MasterSlider = CreateSlider(m_SettingsPanel, 0f, 1f, (val) => { m_MasterLabel.text = $"Master Volume: {(int)(val * 100)}%"; });

            m_SfxLabel = CreateTMP(m_SettingsPanel, "SFX Volume: 80%", 14, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, 20);
            m_SfxSlider = CreateSlider(m_SettingsPanel, 0f, 1f, (val) => { m_SfxLabel.text = $"SFX Volume: {(int)(val * 100)}%"; });

            m_SensitivityLabel = CreateTMP(m_SettingsPanel, "Mouse Sensitivity: 0.15", 14, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, 20);
            m_SensitivitySlider = CreateSlider(m_SettingsPanel, 0.05f, 0.6f, (val) => { m_SensitivityLabel.text = $"Mouse Sensitivity: {val:F2}"; });

            m_FovLabel = CreateTMP(m_SettingsPanel, "Field of View: 75°", 14, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, 20);
            m_FovSlider = CreateSlider(m_SettingsPanel, 60f, 100f, (val) => { m_FovLabel.text = $"Field of View: {(int)val}°"; });

            // Buttons row
            GameObject btnRow = new GameObject("BtnRow");
            btnRow.transform.SetParent(m_SettingsPanel.transform, false);
            var rowLayout = btnRow.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 10;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            var rowRect = btnRow.GetComponent<RectTransform>();
            rowRect.sizeDelta = new Vector2(0, 42);

            CreateButton(btnRow, "APPLY & SAVE", new Color(0.2f, 0.75f, 0.4f), 42, ApplyCurrentSettings);
            CreateButton(btnRow, "DEFAULTS", new Color(0.6f, 0.5f, 0.2f), 42, RestoreDefaults);
            CreateButton(btnRow, "BACK", new Color(0.3f, 0.35f, 0.4f), 42, () => ShowSubPanel(m_MainPausePanel));

            m_SettingsPanel.SetActive(false);
        }

        private void BuildLeaveConfirmPanel(GameObject canvasGO)
        {
            m_LeaveConfirmPanel = new GameObject("LeaveConfirmPanel");
            m_LeaveConfirmPanel.transform.SetParent(canvasGO.transform, false);
            var panelRect = m_LeaveConfirmPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(420, 240);

            var bg = m_LeaveConfirmPanel.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color(0.08f, 0.11f, 0.16f, 0.98f);

            var vLayout = m_LeaveConfirmPanel.AddComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(30, 30, 25, 25);
            vLayout.spacing = 15;
            vLayout.childAlignment = TextAnchor.MiddleCenter;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = false;

            CreateTMP(m_LeaveConfirmPanel, "LEAVE MATCH?", 22, new Color(0.9f, 0.3f, 0.2f), TextAlignmentOptions.Center, FontStyles.Bold, 32);
            CreateTMP(m_LeaveConfirmPanel, "Are you sure you want to leave?\nYou will disconnect from the active match.", 14, Color.white, TextAlignmentOptions.Center, FontStyles.Normal, 40);

            GameObject btnRow = new GameObject("BtnRow");
            btnRow.transform.SetParent(m_LeaveConfirmPanel.transform, false);
            var rowLayout = btnRow.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            var rowRect = btnRow.GetComponent<RectTransform>();
            rowRect.sizeDelta = new Vector2(0, 42);

            CreateButton(btnRow, "CONFIRM LEAVE", new Color(0.85f, 0.25f, 0.25f), 42, () =>
            {
                SetPause(false);
                ConquestSessionManager.Instance?.LeaveSession();
            });

            CreateButton(btnRow, "CANCEL", new Color(0.2f, 0.4f, 0.6f), 42, () => ShowSubPanel(m_MainPausePanel));

            m_LeaveConfirmPanel.SetActive(false);
        }

        private void BuildMatchEndNav(GameObject canvasGO)
        {
            m_MatchEndNavPanel = new GameObject("MatchEndNavPanel");
            m_MatchEndNavPanel.transform.SetParent(canvasGO.transform, false);
            var panelRect = m_MatchEndNavPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.35f);
            panelRect.anchorMax = new Vector2(0.5f, 0.35f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(380, 55);

            // Everyone goes to the results screen and back to the lobby automatically; this only
            // lets a client drop out early. Hidden on the host, where leaving would end the session for all.
            CreateButton(m_MatchEndNavPanel, "LEAVE GAME", new Color(0.3f, 0.35f, 0.4f), 50, () =>
            {
                ConquestSessionManager.Instance?.LeaveSession();
            });

            m_MatchEndNavPanel.SetActive(false);
        }

        private void ShowSubPanel(GameObject activePanel)
        {
            if (m_MainPausePanel != null) m_MainPausePanel.SetActive(activePanel == m_MainPausePanel);
            if (m_SettingsPanel != null) m_SettingsPanel.SetActive(activePanel == m_SettingsPanel);
            if (m_LeaveConfirmPanel != null) m_LeaveConfirmPanel.SetActive(activePanel == m_LeaveConfirmPanel);
        }

        private void Update()
        {
            FindLocalPlayer();

            // Match over: results and the lobby follow automatically
            var mm = ConquestMatchManager.Instance;
            if (mm != null && mm.IsMatchOver)
            {
                bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
                if (m_PauseCanvas != null) m_PauseCanvas.enabled = true;
                if (m_MatchEndNavPanel != null) m_MatchEndNavPanel.SetActive(!isServer);
                if (m_MainPausePanel != null) m_MainPausePanel.SetActive(false);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            // ESC toggle
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (m_SettingsPanel != null && m_SettingsPanel.activeSelf)
                {
                    ShowSubPanel(m_MainPausePanel);
                }
                else if (m_LeaveConfirmPanel != null && m_LeaveConfirmPanel.activeSelf)
                {
                    ShowSubPanel(m_MainPausePanel);
                }
                else
                {
                    TogglePause();
                }
            }
        }

        public void TogglePause()
        {
            SetPause(!m_IsPaused);
        }

        public void SetPause(bool paused)
        {
            m_IsPaused = paused;
            if (m_PauseCanvas != null) m_PauseCanvas.enabled = m_IsPaused;

            ShowSubPanel(m_MainPausePanel);
            if (m_MatchEndNavPanel != null) m_MatchEndNavPanel.SetActive(false);

            if (m_IsPaused)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (m_LocalPlayer != null && m_LocalPlayer.Movement != null)
                {
                    m_LocalPlayer.Movement.SetCursorLock(false);
                }
                LoadSettingsIntoUI();
            }
            else
            {
                if (m_LocalPlayer != null && !m_LocalPlayer.Health.IsDead)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    if (m_LocalPlayer.Movement != null)
                    {
                        m_LocalPlayer.Movement.SetCursorLock(true);
                    }
                }
            }
        }

        private void LoadSettingsIntoUI()
        {
            var sm = ConquestSettingsManager.Instance;
            if (sm == null) return;
            var s = sm.CurrentSettings;

            if (m_MasterSlider != null) m_MasterSlider.value = s.masterVolume;
            if (m_SfxSlider != null) m_SfxSlider.value = s.sfxVolume;
            if (m_SensitivitySlider != null) m_SensitivitySlider.value = s.mouseSensitivity;
            if (m_FovSlider != null) m_FovSlider.value = s.fieldOfView;

            if (m_MasterLabel != null) m_MasterLabel.text = $"Master Volume: {(int)(s.masterVolume * 100)}%";
            if (m_SfxLabel != null) m_SfxLabel.text = $"SFX Volume: {(int)(s.sfxVolume * 100)}%";
            if (m_SensitivityLabel != null) m_SensitivityLabel.text = $"Mouse Sensitivity: {s.mouseSensitivity:F2}";
            if (m_FovLabel != null) m_FovLabel.text = $"Field of View: {(int)s.fieldOfView}°";
        }

        private void ApplyCurrentSettings()
        {
            var sm = ConquestSettingsManager.Instance;
            if (sm == null) return;
            var s = sm.CurrentSettings;

            if (m_MasterSlider != null) s.masterVolume = m_MasterSlider.value;
            if (m_SfxSlider != null) s.sfxVolume = m_SfxSlider.value;
            if (m_SensitivitySlider != null) s.mouseSensitivity = m_SensitivitySlider.value;
            if (m_FovSlider != null) s.fieldOfView = m_FovSlider.value;

            sm.ApplySettings(s);
            ShowSubPanel(m_MainPausePanel);
        }

        private void RestoreDefaults()
        {
            var sm = ConquestSettingsManager.Instance;
            if (sm == null) return;
            sm.RestoreDefaults();
            LoadSettingsIntoUI();
        }

        private TextMeshProUGUI CreateTMP(GameObject parent, string text, float fontSize, Color color, TextAlignmentOptions align, FontStyles style, float height = 30)
        {
            GameObject go = new GameObject("TMP");
            go.transform.SetParent(parent.transform, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = m_Font;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.fontStyle = style;
            return tmp;
        }

        private UnityEngine.UI.Button CreateButton(GameObject parent, string label, Color color, float height, UnityEngine.Events.UnityAction onClick)
        {
            GameObject btnGO = new GameObject($"Btn_{label}");
            btnGO.transform.SetParent(parent.transform, false);
            var le = btnGO.AddComponent<LayoutElement>();
            le.preferredHeight = height;

            var img = btnGO.AddComponent<UnityEngine.UI.Image>();
            img.color = color;

            var btn = btnGO.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(btnGO.transform, false);
            var tr = textGO.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;

            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.font = m_Font;
            tmp.text = label;
            tmp.fontSize = 16;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;

            return btn;
        }

        private Slider CreateSlider(GameObject parent, float min, float max, UnityEngine.Events.UnityAction<float> onValueChanged)
        {
            GameObject sliderGO = new GameObject("Slider");
            sliderGO.transform.SetParent(parent.transform, false);
            var le = sliderGO.AddComponent<LayoutElement>();
            le.preferredHeight = 22;

            var slider = sliderGO.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.onValueChanged.AddListener(onValueChanged);

            // Background
            GameObject bgGO = new GameObject("Background");
            bgGO.transform.SetParent(sliderGO.transform, false);
            var bgImg = bgGO.AddComponent<UnityEngine.UI.Image>();
            bgImg.color = new Color(0.2f, 0.25f, 0.32f);
            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;

            // Fill Area & Fill
            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderGO.transform, false);
            var faRect = fillArea.AddComponent<RectTransform>();
            faRect.anchorMin = new Vector2(0, 0.25f);
            faRect.anchorMax = new Vector2(1, 0.75f);
            faRect.sizeDelta = Vector2.zero;

            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillImg = fill.AddComponent<UnityEngine.UI.Image>();
            fillImg.color = new Color(0.3f, 0.75f, 1.0f);
            slider.fillRect = fill.GetComponent<RectTransform>();

            return slider;
        }

        private void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}

