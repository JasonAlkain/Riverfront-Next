using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Game.Conquest.Maps;
using Game.Conquest.Networking;
using Game.Conquest.Settings;
using Game.Conquest.Gameplay;
using Game.Conquest.Lobby;
using Game.Conquest.Scenes;

namespace Game.Conquest.UI
{
    public enum MenuState
    {
        Title,
        PlaySolo,
        Multiplayer,
        HostGame,
        ServerBrowser,
        JoinByCode,
        Lobby,
        Settings,
        Credits
    }

    public class ConquestMainMenuController : MonoBehaviour
    {
        public static ConquestMainMenuController Instance { get; private set; }

        [Header("Map Registry")]
        [SerializeField] private ConquestMapRegistry mapRegistry;

        [Header("Active Screen")]
        [SerializeField] private MenuState currentMenuState = MenuState.Title;


        [Header("Colors")]
        [SerializeField] private Color primaryColor = new Color(0.35f, 0.78f, 1.0f, 1f);
        [SerializeField] private Color secondaryColor = new Color(0.9f, 0.6f, 0.15f, 1f);


        [Header("Title Colors")]
        [SerializeField] Color playSoloColor = new Color(0.2f, 0.6f, 1.0f);
        [SerializeField] Color multiplayerColor = new Color(0.2f, 0.75f, 0.4f);
        [SerializeField] Color gameSettingsColor = new Color(0.9f, 0.6f, 0.2f);
        [SerializeField] Color creditsColor = new Color(0.22f, 0.28f, 0.36f);
        [SerializeField] Color quitColor = new Color(0.85f, 0.25f, 0.25f);

        [Header("Solo Colors")]
        // Set Colors for the solo panel
        [SerializeField] Color panelColor = new Color(0.12f, 0.16f, 0.24f, 0.9f);
        [SerializeField] Color prevMapColor = new Color(0.25f, 0.30f, 0.40f);
        [SerializeField] Color nextMapColor = new Color(0.25f, 0.30f, 0.40f);
        [SerializeField] Color mapNameColor = Color.yellow;
        [SerializeField] Color descBoxColor = new Color(0.12f, 0.16f, 0.24f, 0.9f);
        [SerializeField] Color easyColor = new Color(0.3f, 0.35f, 0.45f);
        [SerializeField] Color normalColor = new Color(0.8f, 0.6f, 0.2f);
        [SerializeField] Color hardColor = new Color(0.3f, 0.35f, 0.45f);
        [SerializeField] Color teamSizeColor = Color.white;
        [SerializeField] Color ticketsColor = Color.white;
        [SerializeField] Color startBattleColor = new Color(0.2f, 0.75f, 0.4f);
        [SerializeField] Color backColor = new Color(0.3f, 0.35f, 0.4f);

        // ToDO: Planned button color customization
        [Header("Button Colors (WIP)")]
        [SerializeField] Color btnNormal = new Color(0.25f, 0.30f, 0.40f);
        [SerializeField] Color btnHover = new Color(0.35f, 0.45f, 0.55f);
        [SerializeField] Color btnPressed = new Color(0.15f, 0.20f, 0.30f);
        [SerializeField] Color btnDisabled = new Color(0.15f, 0.15f, 0.15f);
        [SerializeField] Color btnTextNormal = Color.white;
        [SerializeField] Color btnTextDisabled = new Color(0.5f, 0.5f, 0.5f);
        

        // Configuration fields for Host / Solo
        private int m_SelectedMapIndex = 0;
        private int m_SoloBotDifficultyIndex = 1; // Normal
        private int m_SoloTeamSize = 8;
        private int m_SoloTickets = 250;

        private string m_HostSessionName = "Alpha Squad Battle";
        private int m_HostMaxPlayers = 16;
        private int m_HostTickets = 250;
        private int m_HostTeamSize = 8;
        private bool m_HostBotFilling = true;
        private int m_HostBotDifficultyIndex = 1; // Normal

        private string m_JoinCodeInput = "127.0.0.1";

        // Canvas & TMP References
        private Canvas m_MainCanvas;
        private CanvasScaler m_CanvasScaler;
        private TMP_FontAsset m_Font;
        private TextMeshProUGUI m_StatusBadgeText;

        // Panel Roots
        private GameObject m_TitlePanel;
        private GameObject m_SoloPanel;
        private GameObject m_MultiplayerPanel;
        private GameObject m_HostPanel;
        private GameObject m_BrowserPanel;
        private GameObject m_JoinCodePanel;
        private GameObject m_LobbyPanel;
        private GameObject m_SettingsPanel;
        private GameObject m_CreditsPanel;

        // Solo Screen Dynamic UI
        private TextMeshProUGUI m_SoloMapTitleText;
        private TextMeshProUGUI m_SoloMapDescText;
        private TextMeshProUGUI m_SoloDiffLabel;
        private TextMeshProUGUI m_SoloTeamSizeLabel;
        private TextMeshProUGUI m_SoloTicketsLabel;

        // Host Screen Dynamic UI
        private TMP_InputField m_HostNameInput;
        private TextMeshProUGUI m_HostMapText;
        private TextMeshProUGUI m_HostMaxPlayersLabel;
        private TextMeshProUGUI m_HostTicketsLabel;
        private TextMeshProUGUI m_HostTeamSizeLabel;
        private TextMeshProUGUI m_HostDiffLabel;

        // Join Code Dynamic UI
        private TMP_InputField m_CodeInput;

        // Lobby Dynamic UI
        private TextMeshProUGUI m_LobbyHeaderText;
        private TextMeshProUGUI m_LobbySubHeaderText;
        private TextMeshProUGUI m_RedTeamListText;
        private TextMeshProUGUI m_BlueTeamListText;
        private GameObject m_LobbyHostControls;

        // Settings Dynamic UI
        private Slider m_SetMasterSlider;
        private Slider m_SetSfxSlider;
        private Slider m_SetMusicSlider;
        private Slider m_SetSensitivitySlider;
        private Slider m_SetFovSlider;
        private Toggle m_SetHighContrastToggle;
        private Toggle m_SetVSyncToggle;
        private TextMeshProUGUI m_SetMasterLabel;
        private TextMeshProUGUI m_SetSfxLabel;
        private TextMeshProUGUI m_SetMusicLabel;
        private TextMeshProUGUI m_SetSensitivityLabel;
        private TextMeshProUGUI m_SetFovLabel;

        private void Awake()
        {
            Instance = this;

            // Ensure persistent services exist if MainMenu is opened directly
            if (ConquestSettingsManager.Instance == null)
            {
                var servicesGO = new GameObject("PersistentServices");
                servicesGO.AddComponent<ConquestSettingsManager>();
                servicesGO.AddComponent<ConquestSessionManager>();
                var stm = servicesGO.AddComponent<ConquestSceneTransitionManager>();
                stm.SetMapRegistry(mapRegistry);
            }

            BuildMenuCanvas();
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (ConquestSessionManager.Instance != null)
            {
                ConquestSessionManager.Instance.OnStatusChanged += HandleStatusChanged;
                HandleStatusChanged(ConquestSessionManager.Instance.StatusMessage);
            }

            SwitchScreen(MenuState.Title);
        }

        private void OnDestroy()
        {
            if (ConquestSessionManager.Instance != null)
            {
                ConquestSessionManager.Instance.OnStatusChanged -= HandleStatusChanged;
            }
        }

        private void HandleStatusChanged(string msg)
        {
            if (m_StatusBadgeText != null)
            {
                m_StatusBadgeText.text = $"[●] {msg}";
            }
        }

        private void Update()
        {
            // Transition to lobby if network connected
            if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
            {
                if (currentMenuState != MenuState.Lobby && (currentMenuState == MenuState.HostGame || currentMenuState == MenuState.JoinByCode || currentMenuState == MenuState.ServerBrowser))
                {
                    SwitchScreen(MenuState.Lobby);
                }
            }

            if (currentMenuState == MenuState.Lobby)
            {
                UpdateLobbyDisplay();
            }
        }

        public void SwitchScreen(MenuState newState)
        {
            currentMenuState = newState;

            if (m_TitlePanel != null) m_TitlePanel.SetActive(newState == MenuState.Title);
            if (m_SoloPanel != null) m_SoloPanel.SetActive(newState == MenuState.PlaySolo);
            if (m_MultiplayerPanel != null) m_MultiplayerPanel.SetActive(newState == MenuState.Multiplayer);
            if (m_HostPanel != null) m_HostPanel.SetActive(newState == MenuState.HostGame);
            if (m_BrowserPanel != null) m_BrowserPanel.SetActive(newState == MenuState.ServerBrowser);
            if (m_JoinCodePanel != null) m_JoinCodePanel.SetActive(newState == MenuState.JoinByCode);
            if (m_LobbyPanel != null) m_LobbyPanel.SetActive(newState == MenuState.Lobby);
            if (m_SettingsPanel != null) m_SettingsPanel.SetActive(newState == MenuState.Settings);
            if (m_CreditsPanel != null) m_CreditsPanel.SetActive(newState == MenuState.Credits);

            if (newState == MenuState.PlaySolo) UpdateSoloDisplay();
            if (newState == MenuState.HostGame) UpdateHostDisplay();
            if (newState == MenuState.Settings) LoadSettingsIntoUI();
            if (newState == MenuState.Lobby) UpdateLobbyDisplay();
        }

        private void BuildMenuCanvas()
        {
            m_Font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (m_Font == null) m_Font = TMP_Settings.defaultFontAsset;

            GameObject canvasGO = new GameObject("MainMenuCanvas");
            canvasGO.transform.SetParent(transform, false);

            m_MainCanvas = canvasGO.AddComponent<Canvas>();
            m_MainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_MainCanvas.sortingOrder = 10;

            m_CanvasScaler = canvasGO.AddComponent<CanvasScaler>();
            m_CanvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            m_CanvasScaler.referenceResolution = new Vector2(1920, 1080);
            m_CanvasScaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            // Fullscreen Backdrop
            GameObject bgGO = new GameObject("Backdrop");
            bgGO.transform.SetParent(canvasGO.transform, false);
            var bgImg = bgGO.AddComponent<UnityEngine.UI.Image>();
            bgImg.color = new Color(0.04f, 0.06f, 0.09f, 1f);
            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;

            // Header Banner
            BuildHeaderBanner(canvasGO);

            // Panels
            m_TitlePanel = BuildTitlePanel(canvasGO);
            m_SoloPanel = BuildSoloPanel(canvasGO);
            m_MultiplayerPanel = BuildMultiplayerPanel(canvasGO);
            m_HostPanel = BuildHostPanel(canvasGO);
            m_BrowserPanel = BuildBrowserPanel(canvasGO);
            m_JoinCodePanel = BuildJoinCodePanel(canvasGO);
            m_LobbyPanel = BuildLobbyPanel(canvasGO);
            m_SettingsPanel = BuildSettingsPanel(canvasGO);
            m_CreditsPanel = BuildCreditsPanel(canvasGO);
        }

        private void BuildHeaderBanner(GameObject canvasGO)
        {
            GameObject header = new GameObject("HeaderBanner");
            header.transform.SetParent(canvasGO.transform, false);
            var hRect = header.AddComponent<RectTransform>();
            hRect.anchorMin = new Vector2(0f, 1f);
            hRect.anchorMax = new Vector2(1f, 1f);
            hRect.pivot = new Vector2(0.5f, 1f);
            hRect.sizeDelta = new Vector2(0, 110);

            var hImg = header.AddComponent<UnityEngine.UI.Image>();
            hImg.color = new Color(0.08f, 0.12f, 0.18f, 0.95f);

            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(header.transform, false);
            var tRect = titleGO.AddComponent<RectTransform>();
            tRect.anchorMin = new Vector2(0f, 0.4f);
            tRect.anchorMax = new Vector2(1f, 0.95f);
            tRect.sizeDelta = Vector2.zero;
            CreateTMP(titleGO, "CONQUEST: FRONTLINE COMMAND", 38, new Color(0.35f, 0.78f, 1.0f), TextAlignmentOptions.Center, FontStyles.Bold);

            GameObject subGO = new GameObject("SubTitle");
            subGO.transform.SetParent(header.transform, false);
            var sRect = subGO.AddComponent<RectTransform>();
            sRect.anchorMin = new Vector2(0f, 0.05f);
            sRect.anchorMax = new Vector2(1f, 0.45f);
            sRect.sizeDelta = Vector2.zero;
            CreateTMP(subGO, "TACTICAL MULTIPLAYER INFANTRY COMBAT • UNITY 6 CANVASES & TEXT MESH PRO", 14, new Color(1.0f, 0.75f, 0.2f), TextAlignmentOptions.Center, FontStyles.Bold);

            GameObject statusGO = new GameObject("StatusBadge");
            statusGO.transform.SetParent(header.transform, false);
            var statRect = statusGO.AddComponent<RectTransform>();
            statRect.anchorMin = new Vector2(0.7f, 0.6f);
            statRect.anchorMax = new Vector2(0.98f, 0.95f);
            statRect.sizeDelta = Vector2.zero;
            m_StatusBadgeText = CreateTMP(statusGO, "[●] Ready", 12, new Color(0.3f, 0.8f, 0.4f), TextAlignmentOptions.Right, FontStyles.Normal);
        }

        // ================= 1. TITLE PANEL =================
        private GameObject BuildTitlePanel(GameObject canvasGO)
        {
            Vector2 windowSize = new Vector2(420, 720);

            GameObject panel = CreateCenterPanel(canvasGO, "TitlePanel", (int)windowSize.x, (int)windowSize.y);
            var vLayout = panel.AddComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(35, 35, 25, 25);
            vLayout.spacing = 14;
            vLayout.childAlignment = TextAnchor.MiddleCenter;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = false;

            CreateTMP(panel, "COMMAND TERMINAL", 24, primaryColor, TextAlignmentOptions.Center, FontStyles.Bold, 36);


            CreateButton(panel, "PLAY SOLO (PRACTICE)", playSoloColor, 52, () => SwitchScreen(MenuState.PlaySolo));
            CreateButton(panel, "MULTIPLAYER OPERATIONS", multiplayerColor, 52, () => SwitchScreen(MenuState.Multiplayer));
            CreateButton(panel, "GAME SETTINGS", gameSettingsColor, 44, () => SwitchScreen(MenuState.Settings));
            CreateButton(panel, "PROJECT CREDITS", creditsColor, 44, () => SwitchScreen(MenuState.Credits));
            CreateButton(panel, "QUIT TO DESKTOP", quitColor, 44, QuitApplication);

            return panel;
        }

        // ================= 2. PLAY SOLO PANEL =================
        private GameObject BuildSoloPanel(GameObject canvasGO)
        {
            // Set window size for the solo panel
            Vector2 windowSize = new Vector2(680, 780);

            GameObject panel = CreateCenterPanel(canvasGO, "SoloPanel", (int)windowSize.x, (int)windowSize.y);
            var vLayout = panel.AddComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(35, 35, 20, 20);
            vLayout.spacing = 10;
            vLayout.childAlignment = TextAnchor.UpperCenter;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = true;

            CreateTMP(panel, "SOLO CONQUEST PRACTICE", 24, panelColor, TextAlignmentOptions.Center, FontStyles.Bold, 34);

            // Map carousel selector
            GameObject mapRow = new GameObject("MapRow");
            mapRow.transform.SetParent(panel.transform, false);
            var hLayout = mapRow.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 10;
            hLayout.childControlWidth = true;
            hLayout.childControlHeight = true;
            mapRow.AddComponent<LayoutElement>().preferredHeight = 44;

            CreateButton(mapRow, "< PREV MAP", prevMapColor, 44, () => CycleMap(-1));
            m_SoloMapTitleText = CreateTMP(mapRow, "BATTLEFIELD NAME", 18, mapNameColor, TextAlignmentOptions.Center, FontStyles.Bold, 44);
            CreateButton(mapRow, "NEXT MAP >", nextMapColor, 44, () => CycleMap(1));

            // Map description box
            GameObject descBox = new GameObject("DescBox");
            descBox.transform.SetParent(panel.transform, false);
            descBox.AddComponent<LayoutElement>().preferredHeight = 75;
            var descImg = descBox.AddComponent<UnityEngine.UI.Image>();
            descImg.color = descBoxColor;
            m_SoloMapDescText = CreateTMP(descBox, "Tactical map briefing...", 13, Color.white, TextAlignmentOptions.Center, FontStyles.Normal, 75);

            // Difficulty buttons row
            m_SoloDiffLabel = CreateTMP(panel, "Bot Difficulty: Normal", 14, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, 20);
            GameObject diffRow = new GameObject("DiffRow");
            diffRow.transform.SetParent(panel.transform, false);
            var dLayout = diffRow.AddComponent<HorizontalLayoutGroup>();
            dLayout.spacing = 10;
            dLayout.childControlWidth = true;
            dLayout.childControlHeight = true;
            diffRow.AddComponent<LayoutElement>().preferredHeight = 36;

            CreateButton(diffRow, "EASY", easyColor, 36, () => SetSoloDifficulty(0));
            CreateButton(diffRow, "NORMAL", normalColor, 36, () => SetSoloDifficulty(1));
            CreateButton(diffRow, "HARD", hardColor, 36, () => SetSoloDifficulty(2));

            // Sliders
            m_SoloTeamSizeLabel = CreateTMP(panel, "Target Team Size: 8 (16 combatants)", 14, teamSizeColor, TextAlignmentOptions.Left, FontStyles.Normal, 20);
            CreateSlider(panel, 4, 16, (val) =>
            {
                m_SoloTeamSize = (int)val;
                if (m_SoloTeamSizeLabel != null) m_SoloTeamSizeLabel.text = $"Target Team Size: {m_SoloTeamSize} ({m_SoloTeamSize * 2} combatants)";
            }, m_SoloTeamSize);

            m_SoloTicketsLabel = CreateTMP(panel, "Reinforcement Tickets: 250", 14, ticketsColor, TextAlignmentOptions.Left, FontStyles.Normal, 20);
            CreateSlider(panel, 100, 350, (val) =>
            {
                m_SoloTickets = (int)val;
                if (m_SoloTicketsLabel != null) m_SoloTicketsLabel.text = $"Reinforcement Tickets: {m_SoloTickets}";
            }, m_SoloTickets);

            // Action row
            GameObject actRow = new GameObject("ActionRow");
            actRow.transform.SetParent(panel.transform, false);
            var aLayout = actRow.AddComponent<HorizontalLayoutGroup>();
            aLayout.spacing = 15;
            aLayout.childControlWidth = true;
            aLayout.childControlHeight = true;
            actRow.AddComponent<LayoutElement>().preferredHeight = 46;

            CreateButton(actRow, "START SOLO BATTLE", startBattleColor, 46, LaunchSoloGame);
            CreateButton(actRow, "BACK", backColor, 46, () => SwitchScreen(MenuState.Title));

            return panel;
        }

        // ================= 3. MULTIPLAYER PANEL =================
        private GameObject BuildMultiplayerPanel(GameObject canvasGO)
        {
            GameObject panel = CreateCenterPanel(canvasGO, "MultiplayerPanel", 460, 420);
            var vLayout = panel.AddComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(35, 35, 25, 25);
            vLayout.spacing = 14;
            vLayout.childAlignment = TextAnchor.MiddleCenter;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = false;

            CreateTMP(panel, "MULTIPLAYER OPERATIONS", 24, new Color(0.35f, 0.78f, 1.0f), TextAlignmentOptions.Center, FontStyles.Bold, 36);

            CreateButton(panel, "HOST NEW GAME", new Color(0.2f, 0.65f, 1.0f), 52, () => SwitchScreen(MenuState.HostGame));
            CreateButton(panel, "BROWSE PUBLIC GAMES", new Color(0.3f, 0.75f, 0.4f), 52, () => SwitchScreen(MenuState.ServerBrowser));
            CreateButton(panel, "JOIN BY CODE / IP", new Color(0.9f, 0.65f, 0.2f), 52, () => SwitchScreen(MenuState.JoinByCode));
            CreateButton(panel, "BACK TO MAIN MENU", new Color(0.3f, 0.35f, 0.4f), 44, () => SwitchScreen(MenuState.Title));

            return panel;
        }

        // ================= 4. HOST GAME PANEL =================
        private GameObject BuildHostPanel(GameObject canvasGO)
        {
            GameObject panel = CreateCenterPanel(canvasGO, "HostPanel", 680, 560);
            var vLayout = panel.AddComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(35, 35, 20, 20);
            vLayout.spacing = 9;
            vLayout.childAlignment = TextAnchor.UpperCenter;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = false;

            CreateTMP(panel, "HOST GAME CONFIGURATION", 24, new Color(0.35f, 0.78f, 1.0f), TextAlignmentOptions.Center, FontStyles.Bold, 32);

            CreateTMP(panel, "Session Name:", 14, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, 18);
            m_HostNameInput = CreateInputField(panel, m_HostSessionName, 38);

            // Map selection
            m_HostMapText = CreateTMP(panel, "Selected Map: Frontline", 14, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, 18);
            GameObject mapRow = new GameObject("HostMapRow");
            mapRow.transform.SetParent(panel.transform, false);
            var hLayout = mapRow.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 10;
            hLayout.childControlWidth = true;
            hLayout.childControlHeight = true;
            mapRow.AddComponent<LayoutElement>().preferredHeight = 36;
            CreateButton(mapRow, "< Prev Map", new Color(0.25f, 0.30f, 0.40f), 36, () => CycleMap(-1));
            CreateButton(mapRow, "Next Map >", new Color(0.25f, 0.30f, 0.40f), 36, () => CycleMap(1));

            m_HostMaxPlayersLabel = CreateTMP(panel, "Max Human Connections: 16", 14, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, 18);
            CreateSlider(panel, 2, 32, (val) =>
            {
                m_HostMaxPlayers = (int)val;
                if (m_HostMaxPlayersLabel != null) m_HostMaxPlayersLabel.text = $"Max Human Connections: {m_HostMaxPlayers}";
            }, m_HostMaxPlayers);

            m_HostTicketsLabel = CreateTMP(panel, "Reinforcement Tickets: 250", 14, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, 18);
            CreateSlider(panel, 100, 350, (val) =>
            {
                m_HostTickets = (int)val;
                if (m_HostTicketsLabel != null) m_HostTicketsLabel.text = $"Reinforcement Tickets: {m_HostTickets}";
            }, m_HostTickets);

            m_HostTeamSizeLabel = CreateTMP(panel, "Target Team Size: 8 (AI Filled)", 14, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, 18);
            CreateSlider(panel, 4, 16, (val) =>
            {
                m_HostTeamSize = (int)val;
                if (m_HostTeamSizeLabel != null) m_HostTeamSizeLabel.text = $"Target Team Size: {m_HostTeamSize} (AI Filled)";
            }, m_HostTeamSize);

            // Actions row
            GameObject actRow = new GameObject("HostActRow");
            actRow.transform.SetParent(panel.transform, false);
            var aLayout = actRow.AddComponent<HorizontalLayoutGroup>();
            aLayout.spacing = 15;
            aLayout.childControlWidth = true;
            aLayout.childControlHeight = true;
            actRow.AddComponent<LayoutElement>().preferredHeight = 46;

            CreateButton(actRow, "CREATE LOBBY & HOST", new Color(0.2f, 0.75f, 0.4f), 46, LaunchHostGame);
            CreateButton(actRow, "BACK", new Color(0.3f, 0.35f, 0.4f), 46, () => SwitchScreen(MenuState.Multiplayer));

            return panel;
        }

        // ================= 5. SERVER BROWSER PANEL =================
        private GameObject BuildBrowserPanel(GameObject canvasGO)
        {
            GameObject panel = CreateCenterPanel(canvasGO, "BrowserPanel", 700, 480);
            var vLayout = panel.AddComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(35, 35, 25, 25);
            vLayout.spacing = 14;
            vLayout.childAlignment = TextAnchor.UpperCenter;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = false;

            CreateTMP(panel, "PUBLIC GAME BROWSER", 24, new Color(0.35f, 0.78f, 1.0f), TextAlignmentOptions.Center, FontStyles.Bold, 34);

            // Local LAN broadcast entry card
            GameObject card = new GameObject("ServerCard");
            card.transform.SetParent(panel.transform, false);
            card.AddComponent<LayoutElement>().preferredHeight = 65;
            var cImg = card.AddComponent<UnityEngine.UI.Image>();
            cImg.color = new Color(0.12f, 0.16f, 0.24f, 0.95f);
            var cLayout = card.AddComponent<HorizontalLayoutGroup>();
            cLayout.padding = new RectOffset(20, 20, 10, 10);
            cLayout.childAlignment = TextAnchor.MiddleCenter;
            cLayout.childControlWidth = true;
            cLayout.childControlHeight = true;

            CreateTMP(card, "<b>Local LAN Server</b>\nConquest Mode (127.0.0.1:7777)", 15, Color.white, TextAlignmentOptions.Left, FontStyles.Normal);
            CreateButton(card, "CONNECT", new Color(0.2f, 0.75f, 0.4f), 42, () =>
            {
                ConquestSessionManager.Instance?.StartClientConnect("127.0.0.1", 7777);
                SwitchScreen(MenuState.Lobby);
            });

            CreateTMP(panel, "Direct IP Connection:", 14, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, 20);
            m_CodeInput = CreateInputField(panel, m_JoinCodeInput, 38);

            GameObject actRow = new GameObject("BrowserActRow");
            actRow.transform.SetParent(panel.transform, false);
            var aLayout = actRow.AddComponent<HorizontalLayoutGroup>();
            aLayout.spacing = 15;
            aLayout.childControlWidth = true;
            aLayout.childControlHeight = true;
            actRow.AddComponent<LayoutElement>().preferredHeight = 44;

            CreateButton(actRow, "CONNECT TO ENTERED IP", new Color(0.2f, 0.65f, 1f), 44, () =>
            {
                string ip = m_CodeInput != null ? m_CodeInput.text : "127.0.0.1";
                ConquestSessionManager.Instance?.StartClientConnect(ip, 7777);
                SwitchScreen(MenuState.Lobby);
            });
            CreateButton(actRow, "BACK", new Color(0.3f, 0.35f, 0.4f), 44, () => SwitchScreen(MenuState.Multiplayer));

            return panel;
        }

        // ================= 6. JOIN BY CODE PANEL =================
        private GameObject BuildJoinCodePanel(GameObject canvasGO)
        {
            GameObject panel = CreateCenterPanel(canvasGO, "JoinCodePanel", 500, 360);
            var vLayout = panel.AddComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(35, 35, 25, 25);
            vLayout.spacing = 15;
            vLayout.childAlignment = TextAnchor.MiddleCenter;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = false;

            CreateTMP(panel, "JOIN BY SESSION CODE / IP", 24, new Color(0.35f, 0.78f, 1.0f), TextAlignmentOptions.Center, FontStyles.Bold, 34);

            CreateTMP(panel, "Enter Server Address or Host Code:", 14, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, 20);
            var codeField = CreateInputField(panel, m_JoinCodeInput, 40);

            GameObject actRow = new GameObject("CodeActRow");
            actRow.transform.SetParent(panel.transform, false);
            var aLayout = actRow.AddComponent<HorizontalLayoutGroup>();
            aLayout.spacing = 15;
            aLayout.childControlWidth = true;
            aLayout.childControlHeight = true;
            actRow.AddComponent<LayoutElement>().preferredHeight = 44;

            CreateButton(actRow, "CONNECT", new Color(0.2f, 0.75f, 0.4f), 44, () =>
            {
                string ip = codeField != null ? codeField.text : "127.0.0.1";
                ConquestSessionManager.Instance?.StartClientConnect(ip, 7777);
                SwitchScreen(MenuState.Lobby);
            });
            CreateButton(actRow, "BACK", new Color(0.3f, 0.35f, 0.4f), 44, () => SwitchScreen(MenuState.Multiplayer));

            return panel;
        }

        // ================= 7. PRE-MATCH LOBBY PANEL =================
        private GameObject BuildLobbyPanel(GameObject canvasGO)
        {
            GameObject panel = CreateCenterPanel(canvasGO, "LobbyPanel", 860, 560);
            var vLayout = panel.AddComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(30, 30, 20, 20);
            vLayout.spacing = 10;
            vLayout.childAlignment = TextAnchor.UpperCenter;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = false;

            m_LobbyHeaderText = CreateTMP(panel, "PRE-MATCH LOBBY", 26, new Color(0.35f, 0.78f, 1.0f), TextAlignmentOptions.Center, FontStyles.Bold, 32);
            m_LobbySubHeaderText = CreateTMP(panel, "MAP: Frontline | TICKETS: 250 | BOTS: 8/team (Normal)", 14, Color.yellow, TextAlignmentOptions.Center, FontStyles.Bold, 22);

            // Red vs Blue Columns
            GameObject teamsRow = new GameObject("TeamsRow");
            teamsRow.transform.SetParent(panel.transform, false);
            var tLayout = teamsRow.AddComponent<HorizontalLayoutGroup>();
            tLayout.spacing = 18;
            tLayout.childControlWidth = true;
            tLayout.childControlHeight = true;
            teamsRow.AddComponent<LayoutElement>().preferredHeight = 240;

            // Red Box
            GameObject redBox = new GameObject("RedBox");
            redBox.transform.SetParent(teamsRow.transform, false);
            var redImg = redBox.AddComponent<UnityEngine.UI.Image>();
            redImg.color = new Color(0.6f, 0.15f, 0.15f, 0.90f);
            var rLayout = redBox.AddComponent<VerticalLayoutGroup>();
            rLayout.padding = new RectOffset(15, 15, 12, 12);
            rLayout.childControlWidth = true;
            CreateTMP(redBox, "RED TEAM", 18, Color.white, TextAlignmentOptions.Center, FontStyles.Bold, 24);
            m_RedTeamListText = CreateTMP(redBox, "• Host [READY]", 14, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, 180);

            // Blue Box
            GameObject blueBox = new GameObject("BlueBox");
            blueBox.transform.SetParent(teamsRow.transform, false);
            var blueImg = blueBox.AddComponent<UnityEngine.UI.Image>();
            blueImg.color = new Color(0.15f, 0.35f, 0.75f, 0.90f);
            var bLayout = blueBox.AddComponent<VerticalLayoutGroup>();
            bLayout.padding = new RectOffset(15, 15, 12, 12);
            bLayout.childControlWidth = true;
            CreateTMP(blueBox, "BLUE TEAM", 18, Color.white, TextAlignmentOptions.Center, FontStyles.Bold, 24);
            m_BlueTeamListText = CreateTMP(blueBox, "• Waiting for players...", 14, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, 180);

            // Player controls row
            GameObject ctrlRow = new GameObject("CtrlRow");
            ctrlRow.transform.SetParent(panel.transform, false);
            var cLayout = ctrlRow.AddComponent<HorizontalLayoutGroup>();
            cLayout.spacing = 10;
            cLayout.childControlWidth = true;
            cLayout.childControlHeight = true;
            ctrlRow.AddComponent<LayoutElement>().preferredHeight = 38;

            CreateButton(ctrlRow, "SWITCH TEAM", new Color(0.25f, 0.35f, 0.5f), 38, () => ConquestLobbyManager.Instance?.SwitchTeamServerRpc());
            CreateButton(ctrlRow, "TOGGLE READY", new Color(0.2f, 0.7f, 0.4f), 38, () => ConquestLobbyManager.Instance?.ToggleReadyServerRpc());
            CreateButton(ctrlRow, "Assault", new Color(0.3f, 0.32f, 0.4f), 38, () => ConquestLobbyManager.Instance?.SelectClassServerRpc(0));
            CreateButton(ctrlRow, "Heavy", new Color(0.3f, 0.32f, 0.4f), 38, () => ConquestLobbyManager.Instance?.SelectClassServerRpc(1));
            CreateButton(ctrlRow, "Engineer", new Color(0.3f, 0.32f, 0.4f), 38, () => ConquestLobbyManager.Instance?.SelectClassServerRpc(2));
            CreateButton(ctrlRow, "Recon", new Color(0.3f, 0.32f, 0.4f), 38, () => ConquestLobbyManager.Instance?.SelectClassServerRpc(3));

            // Host Launch Row
            m_LobbyHostControls = new GameObject("HostLaunchRow");
            m_LobbyHostControls.transform.SetParent(panel.transform, false);
            var hLayout = m_LobbyHostControls.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 14;
            hLayout.childControlWidth = true;
            hLayout.childControlHeight = true;
            m_LobbyHostControls.AddComponent<LayoutElement>().preferredHeight = 48;

            CreateButton(m_LobbyHostControls, "START BATTLE (AUTHORITATIVE)", new Color(0.2f, 0.8f, 0.35f), 48, () => ConquestLobbyManager.Instance?.StartMatch());
            CreateButton(m_LobbyHostControls, "CYCLE MAP", new Color(0.85f, 0.5f, 0.15f), 48, CycleLobbyMap);
            CreateButton(m_LobbyHostControls, "LEAVE LOBBY", new Color(0.85f, 0.25f, 0.25f), 48, () =>
            {
                ConquestSessionManager.Instance?.LeaveSession();
                SwitchScreen(MenuState.Title);
            });

            return panel;
        }

        // ================= 8. SETTINGS PANEL =================
        private GameObject BuildSettingsPanel(GameObject canvasGO)
        {
            GameObject panel = CreateCenterPanel(canvasGO, "SettingsPanel", 660, 520);
            var vLayout = panel.AddComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(35, 35, 20, 20);
            vLayout.spacing = 10;
            vLayout.childAlignment = TextAnchor.UpperCenter;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = false;

            CreateTMP(panel, "SYSTEM SETTINGS", 24, new Color(0.35f, 0.78f, 1.0f), TextAlignmentOptions.Center, FontStyles.Bold, 34);

            m_SetMasterLabel = CreateTMP(panel, "Master Volume: 85%", 14, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, 18);
            m_SetMasterSlider = CreateSlider(panel, 0f, 1f, (val) => { m_SetMasterLabel.text = $"Master Volume: {(int)(val * 100)}%"; }, 0.85f);

            m_SetSfxLabel = CreateTMP(panel, "SFX Volume: 80%", 14, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, 18);
            m_SetSfxSlider = CreateSlider(panel, 0f, 1f, (val) => { m_SetSfxLabel.text = $"SFX Volume: {(int)(val * 100)}%"; }, 0.80f);

            m_SetMusicLabel = CreateTMP(panel, "Music Volume: 70%", 14, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, 18);
            m_SetMusicSlider = CreateSlider(panel, 0f, 1f, (val) => { m_SetMusicLabel.text = $"Music Volume: {(int)(val * 100)}%"; }, 0.70f);

            m_SetSensitivityLabel = CreateTMP(panel, "Mouse Sensitivity: 0.15", 14, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, 18);
            m_SetSensitivitySlider = CreateSlider(panel, 0.05f, 0.6f, (val) => { m_SetSensitivityLabel.text = $"Mouse Sensitivity: {val:F2}"; }, 0.15f);

            m_SetFovLabel = CreateTMP(panel, "Field of View: 75°", 14, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, 18);
            m_SetFovSlider = CreateSlider(panel, 60f, 100f, (val) => { m_SetFovLabel.text = $"Field of View: {(int)val}°"; }, 75f);

            GameObject actRow = new GameObject("SetActRow");
            actRow.transform.SetParent(panel.transform, false);
            var aLayout = actRow.AddComponent<HorizontalLayoutGroup>();
            aLayout.spacing = 14;
            aLayout.childControlWidth = true;
            aLayout.childControlHeight = true;
            actRow.AddComponent<LayoutElement>().preferredHeight = 44;

            CreateButton(actRow, "APPLY & SAVE", new Color(0.2f, 0.75f, 0.4f), 44, SaveAndApplySettings);
            CreateButton(actRow, "DEFAULTS", new Color(0.7f, 0.5f, 0.2f), 44, RestoreSettingsDefaults);
            CreateButton(actRow, "BACK", new Color(0.3f, 0.35f, 0.4f), 44, () => SwitchScreen(MenuState.Title));

            return panel;
        }

        // ================= 9. CREDITS PANEL =================
        private GameObject BuildCreditsPanel(GameObject canvasGO)
        {
            GameObject panel = CreateCenterPanel(canvasGO, "CreditsPanel", 620, 460);
            var vLayout = panel.AddComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(35, 35, 25, 25);
            vLayout.spacing = 15;
            vLayout.childAlignment = TextAnchor.UpperCenter;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = false;

            CreateTMP(panel, "PROJECT CREDITS", 24, new Color(0.35f, 0.78f, 1.0f), TextAlignmentOptions.Center, FontStyles.Bold, 34);

            GameObject box = new GameObject("CreditsBox");
            box.transform.SetParent(panel.transform, false);
            box.AddComponent<LayoutElement>().preferredHeight = 280;
            var bImg = box.AddComponent<UnityEngine.UI.Image>();
            bImg.color = new Color(0.12f, 0.16f, 0.24f, 0.95f);

            CreateTMP(box,
                "<b>CONQUEST: FRONTLINE COMMAND</b>\n" +
                "An original multiplayer tactical shooter inspired by classic conquest mechanics.\n\n" +
                "<b>Gameplay Architecture:</b> Senior Unity 6 Multiplayer Engineer\n" +
                "<b>Networking Framework:</b> Unity Netcode for GameObjects (NGO) & Unity Transport\n" +
                "<b>AI Infantry System:</b> Unity AI Navigation NavMesh Surface & Agents\n" +
                "<b>Audio Architecture:</b> Unity DefaultMixer with Dynamic Rate-Limiter\n" +
                "<b>User Interface:</b> 100% Unity Canvas and Text Mesh Pro\n" +
                "<b>Assets:</b> Free, CC0, and Project Built-In Assets\n\n" +
                "Developed with 100% server authority, client prediction, and scalable bot infantry.",
                14, Color.white, TextAlignmentOptions.Center, FontStyles.Normal, 280);

            CreateButton(panel, "BACK TO MAIN MENU", new Color(0.3f, 0.35f, 0.4f), 44, () => SwitchScreen(MenuState.Title));

            return panel;
        }

        // Helper Builders
        private GameObject CreateCenterPanel(GameObject canvasGO, string name, float width, float height)
        {
            GameObject panel = new GameObject(name);
            panel.transform.SetParent(canvasGO.transform, false);
            var rt = panel.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0, -20);
            rt.sizeDelta = new Vector2(width, height);

            var img = panel.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.08f, 0.11f, 0.16f, 0.98f);
            return panel;
        }

        private TextMeshProUGUI CreateTMP(GameObject parent, string text, float fontSize, Color color, TextAlignmentOptions align, FontStyles style, float height = 30)
        {
            GameObject go = new GameObject("TMP");
            go.transform.SetParent(parent.transform, false);
            if (parent.GetComponent<LayoutGroup>() != null)
            {
                var le = go.AddComponent<LayoutElement>();
                le.preferredHeight = height;
            }
            else
            {
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.sizeDelta = Vector2.zero;
            }

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
            tmp.fontSize = 15;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;

            return btn;
        }

        private Slider CreateSlider(GameObject parent, float min, float max, UnityEngine.Events.UnityAction<float> onValueChanged, float initialValue)
        {
            GameObject sliderGO = new GameObject("Slider");
            sliderGO.transform.SetParent(parent.transform, false);
            var le = sliderGO.AddComponent<LayoutElement>();
            le.preferredHeight = 20;
            le.maxHeight = 32;

            var slider = sliderGO.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = initialValue;
            slider.onValueChanged.AddListener(onValueChanged);

            GameObject bgGO = new GameObject("Background");
            bgGO.transform.SetParent(sliderGO.transform, false);
            var bgImg = bgGO.AddComponent<UnityEngine.UI.Image>();
            bgImg.color = new Color(0.2f, 0.25f, 0.32f);
            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;

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
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0, 0);
            fillRect.anchorMax = new Vector2(1, 1);
            fillRect.sizeDelta = Vector2.zero;


            return slider;
        }

        private TMP_InputField CreateInputField(GameObject parent, string defaultText, float height)
        {
            GameObject fieldGO = new GameObject("InputField");
            fieldGO.transform.SetParent(parent.transform, false);
            var le = fieldGO.AddComponent<LayoutElement>();
            le.preferredHeight = height;

            var img = fieldGO.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.12f, 0.16f, 0.22f);

            var inputField = fieldGO.AddComponent<TMP_InputField>();

            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(fieldGO.transform, false);
            var tr = textGO.AddComponent<RectTransform>();
            tr.anchorMin = new Vector2(0.03f, 0);
            tr.anchorMax = new Vector2(0.97f, 1);
            tr.sizeDelta = Vector2.zero;

            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.font = m_Font;
            tmp.fontSize = 15;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;

            inputField.textComponent = tmp;
            inputField.text = defaultText;

            return inputField;
        }

        // Logical Updaters
        private void CycleMap(int delta)
        {
            var maps = mapRegistry != null ? mapRegistry.RegisteredMaps : null;
            if (maps != null && maps.Count > 0)
            {
                m_SelectedMapIndex = (m_SelectedMapIndex + delta + maps.Count) % maps.Count;
                UpdateSoloDisplay();
                UpdateHostDisplay();
            }
        }

        private void SetSoloDifficulty(int diff)
        {
            m_SoloBotDifficultyIndex = diff;
            if (m_SoloDiffLabel != null) m_SoloDiffLabel.text = $"Bot Difficulty: {(BotDifficulty)diff}";
        }

        private void UpdateSoloDisplay()
        {
            var maps = mapRegistry != null ? mapRegistry.RegisteredMaps : null;
            if (maps != null && maps.Count > 0)
            {
                m_SelectedMapIndex = Mathf.Clamp(m_SelectedMapIndex, 0, maps.Count - 1);
                var map = maps[m_SelectedMapIndex];
                if (m_SoloMapTitleText != null) m_SoloMapTitleText.text = $"<b>{map.displayName.ToUpper()}</b> ({map.commandPostsCount} CPs)";
                if (m_SoloMapDescText != null) m_SoloMapDescText.text = $"{map.description}\nRecommended: {map.recommendedPlayers} Players  |  Scene: {map.sceneName}";
            }
        }

        private void UpdateHostDisplay()
        {
            var maps = mapRegistry != null ? mapRegistry.RegisteredMaps : null;
            if (maps != null && maps.Count > 0)
            {
                m_SelectedMapIndex = Mathf.Clamp(m_SelectedMapIndex, 0, maps.Count - 1);
                var map = maps[m_SelectedMapIndex];
                if (m_HostMapText != null) m_HostMapText.text = $"Selected Map: <b>{map.displayName}</b> ({map.commandPostsCount} Posts)";
            }
        }

        private void LaunchSoloGame()
        {
            var maps = mapRegistry != null ? mapRegistry.RegisteredMaps : null;
            var map = maps != null && maps.Count > 0 ? maps[m_SelectedMapIndex] : null;
            string sceneName = map != null ? map.sceneName : "ConquestLargeBattlefield";
            ConquestSessionManager.Instance?.StartSoloGame(sceneName, m_SoloTickets, m_SoloTeamSize, (BotDifficulty)m_SoloBotDifficultyIndex);
        }

        private void LaunchHostGame()
        {
            var maps = mapRegistry != null ? mapRegistry.RegisteredMaps : null;
            var map = maps != null && maps.Count > 0 ? maps[m_SelectedMapIndex] : null;
            string sceneName = map != null ? map.sceneName : "ConquestLargeBattlefield";
            string mapId = map != null ? map.mapId : "the_frontline";
            string sName = m_HostNameInput != null ? m_HostNameInput.text : m_HostSessionName;

            bool success = ConquestSessionManager.Instance != null && ConquestSessionManager.Instance.StartHostGame(
                mapId, sceneName, sName, m_HostMaxPlayers, m_HostTickets, m_HostTeamSize, m_HostBotFilling, (BotDifficulty)m_HostBotDifficultyIndex);

            if (success)
            {
                SwitchScreen(MenuState.Lobby);
            }
        }

        private void UpdateLobbyDisplay()
        {
            var lobby = ConquestLobbyManager.Instance;
            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

            if (m_LobbyHostControls != null) m_LobbyHostControls.SetActive(isHost);

            if (lobby != null)
            {
                if (m_LobbyHeaderText != null) m_LobbyHeaderText.text = $"LOBBY: {lobby.sessionName.Value.ToString().ToUpper()}";
                if (m_LobbySubHeaderText != null)
                {
                    string sceneName = lobby.selectedMapScene.Value.ToString();
                    var mapData = mapRegistry != null ? mapRegistry.GetMapBySceneName(sceneName) : null;
                    string mapLabel = mapData != null && mapData.sceneName == sceneName ? mapData.displayName : sceneName;
                    m_LobbySubHeaderText.text = $"MAP: {mapLabel} | TICKETS: {lobby.reinforcementTickets.Value} | BOTS: {lobby.targetTeamSize.Value}/team ({lobby.botDifficulty.Value})";
                }

                string redList = "";
                string blueList = "";

                if (lobby.lobbyPlayers != null)
                {
                    for (int i = 0; i < lobby.lobbyPlayers.Count; i++)
                    {
                        var p = lobby.lobbyPlayers[i];
                        string readyStr = p.isReady ? "<color=green>[READY]</color>" : "<color=yellow>[WAITING]</color>";
                        string hostTag = p.isHost ? " (Host)" : "";
                        string line = $"• {p.playerName}{hostTag} - Class {p.classIndex} {readyStr}\n";

                        if (p.team == Team.Red) redList += line;
                        else if (p.team == Team.Blue) blueList += line;
                    }
                }

                if (m_RedTeamListText != null) m_RedTeamListText.text = string.IsNullOrEmpty(redList) ? "No players" : redList;
                if (m_BlueTeamListText != null) m_BlueTeamListText.text = string.IsNullOrEmpty(blueList) ? "No players" : blueList;
            }
        }

        private void CycleLobbyMap()
        {
            var maps = mapRegistry != null ? mapRegistry.RegisteredMaps : null;
            if (maps != null && maps.Count > 0)
            {
                m_SelectedMapIndex = (m_SelectedMapIndex + 1) % maps.Count;
                ConquestLobbyManager.Instance?.SetMapSceneServerRpc(maps[m_SelectedMapIndex].sceneName);
            }
        }

        private void LoadSettingsIntoUI()
        {
            var sm = ConquestSettingsManager.Instance;
            if (sm == null) return;
            var s = sm.CurrentSettings;

            if (m_SetMasterSlider != null) m_SetMasterSlider.value = s.masterVolume;
            if (m_SetSfxSlider != null) m_SetSfxSlider.value = s.sfxVolume;
            if (m_SetMusicSlider != null) m_SetMusicSlider.value = s.musicVolume;
            if (m_SetSensitivitySlider != null) m_SetSensitivitySlider.value = s.mouseSensitivity;
            if (m_SetFovSlider != null) m_SetFovSlider.value = s.fieldOfView;
        }

        private void SaveAndApplySettings()
        {
            var sm = ConquestSettingsManager.Instance;
            if (sm == null) return;
            var s = sm.CurrentSettings;

            if (m_SetMasterSlider != null) s.masterVolume = m_SetMasterSlider.value;
            if (m_SetSfxSlider != null) s.sfxVolume = m_SetSfxSlider.value;
            if (m_SetMusicSlider != null) s.musicVolume = m_SetMusicSlider.value;
            if (m_SetSensitivitySlider != null) s.mouseSensitivity = m_SetSensitivitySlider.value;
            if (m_SetFovSlider != null) s.fieldOfView = m_SetFovSlider.value;

            sm.ApplySettings(s);
            SwitchScreen(MenuState.Title);
        }

        private void RestoreSettingsDefaults()
        {
            var sm = ConquestSettingsManager.Instance;
            if (sm == null) return;
            sm.RestoreDefaults();
            LoadSettingsIntoUI();
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

