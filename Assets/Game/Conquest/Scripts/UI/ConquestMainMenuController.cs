using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;
using Game.Conquest.Classes;
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
        JoinByCode, // Kept so serialized indices stay stable; routed to ServerBrowser (direct IP lives there).
        Lobby,
        Settings,
        Credits,
        PlaySoloMenu // The Play Solo submenu (Tutorial / Practice / Campaign). PlaySolo is the solo match setup.
    }

    /// <summary>
    /// Drives the MainMenuCanvas prefab: binds the panel components' controls to menu actions and
    /// switches between panels. Layout and visuals live in the prefab, not in code.
    /// </summary>
    public class ConquestMainMenuController : MonoBehaviour
    {
        public static ConquestMainMenuController Instance { get; private set; }

        [Header("Map Registry")]
        [SerializeField] private ConquestMapRegistry mapRegistry;

        [Header("Classes")]
        [Tooltip("Shared class list, for the class names on the lobby player cards.")]
        [SerializeField] private ConquestClassRoster classRoster;

        [Header("Active Screen")]
        [SerializeField] private MenuState currentMenuState = MenuState.Title;

        [Header("Panel UI References")]
        [SerializeField] private HeaderBannerUI headerBannerUI;
        [SerializeField] private TitlePanelUI titlePanelUI;
        [Tooltip("Found in the menu canvas when left empty.")]
        [SerializeField] private PlaySoloMenuPanelUI playSoloMenuPanelUI;
        [SerializeField] private SoloPanelUI soloPanelUI;
        [SerializeField] private MultiplayerPanelUI multiplayerPanelUI;
        [SerializeField] private HostPanelUI hostPanelUI;
        [SerializeField] private BrowserPanelUI browserPanelUI;
        //[SerializeField] private JoinCodePanelUI joinCodePanelUI;
        [SerializeField] private LobbyPanelUI lobbyPanelUI;
        [SerializeField] private SettingsPanelUI settingsPanelUI;
        [SerializeField] private CreditsPanelUI creditsPanelUI;

        [Header("Persistent Services Fallback")]
        [Tooltip("Handed to the scene transition manager when the menu is opened directly, without Bootstrap.")]
        [SerializeField] private LoadingScreenUI loadingScreenPrefab;

        // Configuration fields for Host / Solo
        private int m_SelectedMapIndex = 0;
        private int m_SoloBotDifficultyIndex = 1; // Normal
        private int m_SoloTeamSize = 8;
        private int m_SoloTickets = 250;
        // 0 Auto (smaller team), 1 Red, 2 Blue.
        private int m_SoloTeamIndex = 0;
        private static readonly string[] k_SoloTeams = { "Auto", "Red Team", "Blue Team" };

        private string m_HostSessionName = "Alpha Squad Battle";
        private int m_HostMaxPlayers = 16;
        private int m_HostTickets = 250;
        private int m_HostTeamSize = 8;
        private bool m_HostBotFilling = true;
        // Online hosts through Relay (players join with a code); off hosts on the LAN by IP.
        private bool m_HostOnline = true;
        private bool m_HostStarting;
        private int m_HostBotDifficultyIndex = 1; // Normal

        private string m_JoinCodeInput = "127.0.0.1";

        // Direct connect state
        private readonly List<string> m_RecentServers = new List<string>();
        private bool m_IsConnecting = false;
        private bool m_LeavingIntentionally = false;
        private NetworkManager m_SubscribedNetworkManager;

        private const int MaxRecentServers = 3;
        private const string RecentServersPrefsKey = "Conquest.RecentServers";

        private static readonly string[] k_OnOff = { "Off", "On" };
        private static readonly string[] k_NetworkModes = { "LAN (direct IP)", "Online (join code)" };
        private static readonly string[] k_Difficulties = { "Easy", "Normal", "Hard" };

        // Lobby state
        // What the lobby's join box shows, resolved once per lobby visit.
        private bool m_HasShareInfo;
        private string m_ShareLabel = "", m_ShareShown = "", m_ShareCopy = "";
        private bool m_LobbyNameSent = false;
        private int m_LobbyDisplayHash;
        private bool m_LobbyDisplayValid;

        private void Awake()
        {
            // Ensure persistent services exist if MainMenu is opened directly
            if (ConquestSettingsManager.Instance == null)
            {
                var servicesGO = new GameObject("PersistentServices");
                servicesGO.AddComponent<ConquestSettingsManager>();
                servicesGO.AddComponent<ConquestSessionManager>();
                var stm = servicesGO.AddComponent<ConquestSceneTransitionManager>();
                stm.SetMapRegistry(mapRegistry);
                stm.SetLoadingScreenPrefab(loadingScreenPrefab);
            }

            // A dedicated server shows no menus. The lobby itself runs on ConquestLobbyManager,
            // which stays active in this scene.
            if (ConquestServerLaunchArgs.IsDedicatedServerLaunch())
            {
                enabled = false;
                return;
            }

            Instance = this;

            // One-time lookup (not per frame) in case the scene hasn't assigned the header yet.
            if (headerBannerUI == null)
            {
                headerBannerUI = FindAnyObjectByType<HeaderBannerUI>(FindObjectsInactive.Include);
            }
            if (playSoloMenuPanelUI == null)
            {
                playSoloMenuPanelUI = FindAnyObjectByType<PlaySoloMenuPanelUI>(FindObjectsInactive.Include);
            }

            BindTitlePanel();
            BindPlaySoloMenuPanel();
            BindSoloPanel();
            BindMultiplayerPanel();
            BindHostPanel();
            BindBrowserPanel();
            BindLobbyPanel();
            BindSettingsPanel();
            BindCreditsPanel();
            ApplySelectedMapDefaults();
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            var session = ConquestSession.Current;
            if (session != null)
            {
                session.OnStatusChanged += HandleStatusChanged;
                session.OnOnlineJoinFailed += HandleOnlineJoinFailed;
                HandleStatusChanged(session.StatusMessage);
            }

            SubscribeNetworkCallbacks();
            SwitchScreen(MenuState.Title);
        }

        private void OnDestroy()
        {
            UnsubscribeNetworkCallbacks();
            if (ConquestSession.Current != null)
            {
                ConquestSession.Current.OnStatusChanged -= HandleStatusChanged;
                ConquestSession.Current.OnOnlineJoinFailed -= HandleOnlineJoinFailed;
            }
            if (Instance == this) Instance = null;
        }

        private void HandleStatusChanged(string msg)
        {
            SetStatusBadge(msg);
        }

        private void HandleOnlineJoinFailed(string reason)
        {
            if (!m_IsConnecting) return;
            m_IsConnecting = false;
            m_LeavingIntentionally = false;
            ShowJoinError(reason);
        }

        private void SetStatusBadge(string msg)
        {
            if (headerBannerUI != null && headerBannerUI.txt_StatusBadge != null)
            {
                headerBannerUI.txt_StatusBadge.text = $"[●] {msg}";
            }
        }

        private void Update()
        {
            SubscribeNetworkCallbacks();
            HandleCancel();

            // Enter the lobby only once actually connected. IsClient is already true while a
            // connection attempt is still pending, which used to strand players on an empty lobby.
            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsServer || nm.IsConnectedClient))
            {
                if (currentMenuState == MenuState.HostGame || currentMenuState == MenuState.ServerBrowser)
                {
                    m_IsConnecting = false;
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
            if (newState == MenuState.JoinByCode) newState = MenuState.ServerBrowser;

            MenuState previousState = currentMenuState;
            currentMenuState = newState;

            SetPanelVisible(titlePanelUI, newState == MenuState.Title);
            if (headerBannerUI != null) headerBannerUI.SetTitleVisible(newState != MenuState.Title);
            SetPanelVisible(playSoloMenuPanelUI, newState == MenuState.PlaySoloMenu);
            SetPanelVisible(soloPanelUI, newState == MenuState.PlaySolo);
            SetPanelVisible(multiplayerPanelUI, newState == MenuState.Multiplayer);
            SetPanelVisible(hostPanelUI, newState == MenuState.HostGame);
            SetPanelVisible(browserPanelUI, newState == MenuState.ServerBrowser);
            SetPanelVisible(lobbyPanelUI, newState == MenuState.Lobby);
            SetPanelVisible(settingsPanelUI, newState == MenuState.Settings);
            SetPanelVisible(creditsPanelUI, newState == MenuState.Credits);

            if (newState == MenuState.PlaySolo) UpdateSoloDisplay();
            FocusForPlaySolo(previousState, newState);
            if (newState == MenuState.HostGame) UpdateHostDisplay();
            if (newState == MenuState.ServerBrowser) RefreshBrowserPanel();
            if (newState == MenuState.Lobby)
            {
                // Enumerating interfaces allocates, so resolve the join address once per lobby visit.
                m_HasShareInfo = ConquestSession.Current != null &&
                                 ConquestSession.Current.TryGetShareInfo(out m_ShareLabel, out m_ShareShown, out m_ShareCopy);
                m_LobbyNameSent = false;
                m_LobbyDisplayValid = false;
                if (lobbyPanelUI != null && lobbyPanelUI.tmp_PlayerName != null)
                {
                    lobbyPanelUI.tmp_PlayerName.text = LoadSavedPlayerName();
                }
                UpdateLobbyDisplay();
            }
        }

        // A controller needs something selected to navigate. The Play Solo screens pick a sensible
        // button; coming back to the title focuses the button that led there.
        private void FocusForPlaySolo(MenuState from, MenuState to)
        {
            Button focus = null;
            if (to == MenuState.PlaySoloMenu && playSoloMenuPanelUI != null)
            {
                // Tutorial comes first but can't be selected yet.
                focus = playSoloMenuPanelUI.btn_Practice;
            }
            else if (to == MenuState.PlaySolo && soloPanelUI != null)
            {
                focus = soloPanelUI.btn_Start;
            }
            else if (to == MenuState.Title && from == MenuState.PlaySoloMenu && titlePanelUI != null)
            {
                focus = titlePanelUI.btn_Singleplayer;
            }
            if (focus != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(focus.gameObject);
        }

        // Cancel (Esc / pad B) steps back, only on the Play Solo screens: other screens have text
        // fields, and their Back buttons leave sessions or cancel connections.
        private void HandleCancel()
        {
            if (currentMenuState != MenuState.PlaySoloMenu && currentMenuState != MenuState.PlaySolo) return;
            var module = EventSystem.current != null ? EventSystem.current.currentInputModule as InputSystemUIInputModule : null;
            var cancel = module != null && module.cancel != null ? module.cancel.action : null;
            if (cancel == null || !cancel.WasPressedThisFrame()) return;
            SwitchScreen(currentMenuState == MenuState.PlaySolo ? MenuState.PlaySoloMenu : MenuState.Title);
        }

        private static void SetPanelVisible(MonoBehaviour panel, bool visible)
        {
            if (panel != null) panel.gameObject.SetActive(visible);
        }

        // ================= Panel binding =================
        // The prefab is a static layout: button/slider listeners are attached here, not serialized.

        private static void Bind(Button button, UnityEngine.Events.UnityAction onClick)
        {
            if (button != null) button.onClick.AddListener(onClick);
        }

        private static void BindSlider(Slider slider, bool wholeNumbers, float value, UnityEngine.Events.UnityAction<float> onChanged)
        {
            if (slider == null) return;
            slider.wholeNumbers = wholeNumbers;
            slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(onChanged);
        }

        private static void SetText(TextMeshProUGUI label, string text)
        {
            if (label != null) label.text = text;
        }

        private void BindTitlePanel()
        {
            if (titlePanelUI == null) return;
            Bind(titlePanelUI.btn_Singleplayer, () => SwitchScreen(MenuState.PlaySoloMenu));
            Bind(titlePanelUI.btn_Multiplayer, () => SwitchScreen(MenuState.Multiplayer));
            Bind(titlePanelUI.btn_Settings, () => SwitchScreen(MenuState.Settings));
            Bind(titlePanelUI.btn_Credits, () => SwitchScreen(MenuState.Credits));
            Bind(titlePanelUI.btn_Quit, QuitApplication);
        }

        private void BindPlaySoloMenuPanel()
        {
            var p = playSoloMenuPanelUI;
            if (p == null) return;
            Bind(p.btn_Practice, () => SwitchScreen(MenuState.PlaySolo));
            Bind(p.btn_Back, () => SwitchScreen(MenuState.Title));
            // Tutorial and Campaign are coming later: visible, but nothing to select.
            if (p.btn_Tutorial != null) p.btn_Tutorial.interactable = false;
            if (p.btn_Campaign != null) p.btn_Campaign.interactable = false;
        }

        private void BindSoloPanel()
        {
            var p = soloPanelUI;
            if (p == null) return;

            Bind(p.btn_PrevMap, () => CycleMap(-1));
            Bind(p.btn_NextMap, () => CycleMap(1));
            Bind(p.btn_Start, LaunchSoloGame);
            Bind(p.btn_Back, () => SwitchScreen(MenuState.PlaySoloMenu));

            if (p.stp_Team != null)
            {
                p.stp_Team.SetOptions(k_SoloTeams, m_SoloTeamIndex);
                p.stp_Team.Changed += index => m_SoloTeamIndex = index;
            }

            BindDifficultyToggle(p.tgl_Easy, 0);
            BindDifficultyToggle(p.tgl_Normal, 1);
            BindDifficultyToggle(p.tgl_Hard, 2);
            SetSoloDifficulty(m_SoloBotDifficultyIndex);

            BindSlider(p.sld_TargetTeamSize, true, m_SoloTeamSize, val =>
            {
                m_SoloTeamSize = Mathf.RoundToInt(val);
                UpdateSoloLabels();
            });
            BindSlider(p.sld_TeamTickets, true, m_SoloTickets, val =>
            {
                m_SoloTickets = Mathf.RoundToInt(val);
                UpdateSoloLabels();
            });
            UpdateSoloLabels();
        }

        private void BindDifficultyToggle(Toggle toggle, int difficulty)
        {
            if (toggle == null) return;
            toggle.SetIsOnWithoutNotify(difficulty == m_SoloBotDifficultyIndex);
            toggle.onValueChanged.AddListener(isOn =>
            {
                if (isOn) SetSoloDifficulty(difficulty);
            });
        }

        private void BindMultiplayerPanel()
        {
            var p = multiplayerPanelUI;
            if (p == null) return;
            Bind(p.btn_Host, () => SwitchScreen(MenuState.HostGame));
            // No public browser or join codes yet: both routes open the browser panel, whose direct-IP
            // field is the only join path for now (Steam browser comes later).
            Bind(p.btn_BrowseGames, () => SwitchScreen(MenuState.ServerBrowser));
            Bind(p.btn_Join, () => SwitchScreen(MenuState.ServerBrowser));
            Bind(p.btn_Back, () => SwitchScreen(MenuState.Title));
        }

        private void BindHostPanel()
        {
            var p = hostPanelUI;
            if (p == null) return;

            if (p.tmp_SessionName != null) p.tmp_SessionName.text = m_HostSessionName;

            Bind(p.btn_PrevMap, () => CycleMap(-1));
            Bind(p.btn_NextMap, () => CycleMap(1));
            Bind(p.btn_CreateLobby, LaunchHostGame);
            Bind(p.btn_Back, () => SwitchScreen(MenuState.Multiplayer));

            BindSlider(p.sld_MaxHumanConnections, true, m_HostMaxPlayers, val =>
            {
                m_HostMaxPlayers = Mathf.RoundToInt(val);
                UpdateHostLabels();
            });
            BindSlider(p.sld_ReinforcementTickets, true, m_HostTickets, val =>
            {
                m_HostTickets = Mathf.RoundToInt(val);
                UpdateHostLabels();
            });
            BindSlider(p.sld_TargetTeamSize, true, m_HostTeamSize, val =>
            {
                m_HostTeamSize = Mathf.RoundToInt(val);
                UpdateHostLabels();
            });
            if (p.stp_BotFill != null)
            {
                p.stp_BotFill.SetOptions(k_OnOff, m_HostBotFilling ? 1 : 0);
                p.stp_BotFill.Changed += index =>
                {
                    m_HostBotFilling = index == 1;
                    UpdateHostLabels();
                };
            }
            if (p.stp_Network != null)
            {
                p.stp_Network.SetOptions(k_NetworkModes, m_HostOnline ? 1 : 0);
                p.stp_Network.Changed += index => m_HostOnline = index == 1;
            }
            if (p.stp_BotDifficulty != null)
            {
                p.stp_BotDifficulty.SetOptions(k_Difficulties, m_HostBotDifficultyIndex);
                p.stp_BotDifficulty.Changed += index => m_HostBotDifficultyIndex = index;
            }
            UpdateHostLabels();
        }

        private void BindBrowserPanel()
        {
            var p = browserPanelUI;
            if (p == null) return;

            if (p.tmp_DirectAddress != null) p.tmp_DirectAddress.text = m_JoinCodeInput;

            Bind(p.btn_Connect, ConnectToEnteredAddress);
            Bind(p.btn_ConnectDirect, ConnectToEnteredAddress);
            Bind(p.btn_Refresh, RefreshBrowserPanel);
            Bind(p.btn_Host, () => SwitchScreen(MenuState.HostGame));
            // While a connection is pending, Back cancels it instead of leaving the screen.
            Bind(p.btn_Back, () =>
            {
                if (m_IsConnecting) CancelConnect();
                else SwitchScreen(MenuState.Multiplayer);
            });
        }

        private void BindLobbyPanel()
        {
            var p = lobbyPanelUI;
            if (p == null) return;

            Bind(p.btn_SwitchTeam, () => ConquestLobbyManager.Instance?.SwitchTeamServerRpc());
            Bind(p.btn_LeaveLobby, LeaveLobby);
            Bind(p.btn_ToggleReady, () => ConquestLobbyManager.Instance?.ToggleReadyServerRpc());
            Bind(p.btn_StartMatch, () => ConquestLobbyManager.Instance?.StartMatch());
            Bind(p.btn_CycleMap, CycleLobbyMap);
            Bind(p.btn_SetName, SubmitLobbyName);

            if (p.tmp_PlayerName != null)
            {
                p.tmp_PlayerName.characterLimit = ConquestSessionManager.MaxPlayerNameLength;
                p.tmp_PlayerName.text = LoadSavedPlayerName();
            }
        }

        // The Settings screen loads, applies and saves on its own; the menu only handles leaving it.
        private void BindSettingsPanel()
        {
            if (settingsPanelUI != null) settingsPanelUI.Closed += () => SwitchScreen(MenuState.Title);
        }

        private void BindCreditsPanel()
        {
            if (creditsPanelUI == null) return;
            Bind(creditsPanelUI.btn_Back, () => SwitchScreen(MenuState.Title));
        }

        // ================= Logical updaters =================

        private void CycleMap(int delta)
        {
            var maps = mapRegistry != null ? mapRegistry.RegisteredMaps : null;
            if (maps != null && maps.Count > 0)
            {
                m_SelectedMapIndex = (m_SelectedMapIndex + delta + maps.Count) % maps.Count;
                ApplySelectedMapDefaults();
                UpdateSoloDisplay();
                UpdateHostDisplay();
            }
        }

        // Picking a map pre-fills its tickets and team size; the host can still change them.
        private void ApplySelectedMapDefaults()
        {
            var maps = mapRegistry != null ? mapRegistry.RegisteredMaps : null;
            if (maps == null || maps.Count == 0) return;

            m_SelectedMapIndex = Mathf.Clamp(m_SelectedMapIndex, 0, maps.Count - 1);
            var settings = ConquestMatchSettings.ForMap(maps[m_SelectedMapIndex]);

            m_SoloTickets = PrefillSlider(soloPanelUI != null ? soloPanelUI.sld_TeamTickets : null, settings.reinforcementTickets);
            m_SoloTeamSize = PrefillSlider(soloPanelUI != null ? soloPanelUI.sld_TargetTeamSize : null, settings.teamSize);
            m_HostTickets = PrefillSlider(hostPanelUI != null ? hostPanelUI.sld_ReinforcementTickets : null, settings.reinforcementTickets);
            m_HostTeamSize = PrefillSlider(hostPanelUI != null ? hostPanelUI.sld_TargetTeamSize : null, settings.teamSize);
            UpdateSoloLabels();
            UpdateHostLabels();
        }

        // Returns what the slider actually shows, in case its range clamped the value.
        private static int PrefillSlider(Slider slider, int value)
        {
            if (slider == null) return value;
            slider.SetValueWithoutNotify(value);
            return Mathf.RoundToInt(slider.value);
        }

        private void SetSoloDifficulty(int diff)
        {
            m_SoloBotDifficultyIndex = diff;
            if (soloPanelUI != null) SetText(soloPanelUI.txt_Difficulty, ((BotDifficulty)diff).ToString());
        }

        private void UpdateSoloLabels()
        {
            if (soloPanelUI == null) return;
            SetText(soloPanelUI.txt_TeamSize, m_SoloTeamSize.ToString());
            SetText(soloPanelUI.txt_Tickets, m_SoloTickets.ToString());
        }

        private void UpdateHostLabels()
        {
            if (hostPanelUI == null) return;
            SetText(hostPanelUI.txt_MaxHumanConnections, m_HostMaxPlayers.ToString());
            SetText(hostPanelUI.txt_ReinforcementTickets, m_HostTickets.ToString());
            SetText(hostPanelUI.txt_TargetTeamSize, m_HostTeamSize.ToString());
        }

        private void UpdateSoloDisplay()
        {
            if (soloPanelUI == null) return;
            var maps = mapRegistry != null ? mapRegistry.RegisteredMaps : null;
            if (maps != null && maps.Count > 0)
            {
                m_SelectedMapIndex = Mathf.Clamp(m_SelectedMapIndex, 0, maps.Count - 1);
                var map = maps[m_SelectedMapIndex];
                SetText(soloPanelUI.txt_MapName, $"{map.displayName} ({map.commandPostsCount} Posts)");
                SetText(soloPanelUI.txt_MapDescription, $"{map.description} Recommended: {map.recommendedPlayers} players.");
                ShowMapPreview(soloPanelUI.img_MapPreview, map);
            }
        }

        private void UpdateHostDisplay()
        {
            if (hostPanelUI == null) return;
            var maps = mapRegistry != null ? mapRegistry.RegisteredMaps : null;
            if (maps != null && maps.Count > 0)
            {
                m_SelectedMapIndex = Mathf.Clamp(m_SelectedMapIndex, 0, maps.Count - 1);
                var map = maps[m_SelectedMapIndex];
                SetText(hostPanelUI.txt_SelectedMap, $"{map.displayName} ({map.commandPostsCount} Posts)");
                SetText(hostPanelUI.txt_MapDescription, map.description);
                ShowMapPreview(hostPanelUI.img_MapPreview, map);
            }
        }

        // A map's preview image, or a dark tint of its colour while it has none (placeholder).
        private static void ShowMapPreview(Image image, Game.Conquest.Maps.ConquestMapData map)
        {
            if (image == null || map == null) return;
            image.sprite = map.previewImage;
            Color c = map.themeColor;
            image.color = map.previewImage != null ? Color.white : new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.45f, 1f);
        }

        private void LaunchSoloGame()
        {
            var maps = mapRegistry != null ? mapRegistry.RegisteredMaps : null;
            var map = maps != null && maps.Count > 0 ? maps[m_SelectedMapIndex] : null;
            string sceneName = map != null ? map.sceneName : "ConquestLargeBattlefield";
            Team team = m_SoloTeamIndex == 1 ? Team.Red : m_SoloTeamIndex == 2 ? Team.Blue : Team.Neutral;
            ConquestSession.Current?.StartSoloGame(sceneName, m_SoloTickets, m_SoloTeamSize, (BotDifficulty)m_SoloBotDifficultyIndex, team);
        }

        private void LaunchHostGame()
        {
            var maps = mapRegistry != null ? mapRegistry.RegisteredMaps : null;
            var map = maps != null && maps.Count > 0 ? maps[m_SelectedMapIndex] : null;
            string sceneName = map != null ? map.sceneName : "ConquestLargeBattlefield";
            string mapId = map != null ? map.mapId : "the_frontline";
            var nameInput = hostPanelUI != null ? hostPanelUI.tmp_SessionName : null;
            string sName = nameInput != null && !string.IsNullOrWhiteSpace(nameInput.text) ? nameInput.text : m_HostSessionName;

            var session = ConquestSession.Current;
            if (session == null || m_HostStarting) return;

            if (m_HostOnline)
            {
                // Signing in and reserving the relay take a moment; Update moves to the lobby once
                // the host is running, so only a failure needs handling here.
                SetHostStarting(true);
                session.StartOnlineHostGame(mapId, sceneName, sName, m_HostMaxPlayers, m_HostTickets, m_HostTeamSize,
                    m_HostBotFilling, (BotDifficulty)m_HostBotDifficultyIndex, (ok, message) =>
                    {
                        SetHostStarting(false);
                        if (!ok) SetStatusBadge(message);
                    });
                return;
            }

            bool success = session.StartHostGame(
                mapId, sceneName, sName, m_HostMaxPlayers, m_HostTickets, m_HostTeamSize, m_HostBotFilling, (BotDifficulty)m_HostBotDifficultyIndex);

            if (success)
            {
                SwitchScreen(MenuState.Lobby);
            }
        }

        private void SetHostStarting(bool starting)
        {
            m_HostStarting = starting;
            if (hostPanelUI != null && hostPanelUI.btn_CreateLobby != null) hostPanelUI.btn_CreateLobby.interactable = !starting;
        }

        private void UpdateLobbyDisplay()
        {
            var lobby = ConquestLobbyManager.Instance;
            var p = lobbyPanelUI;
            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

            if (p != null)
            {
                // Leave Lobby stays visible for everyone; only host launch controls are gated.
                if (p.btn_StartMatch != null) p.btn_StartMatch.gameObject.SetActive(isHost);
                if (p.btn_CycleMap != null) p.btn_CycleMap.gameObject.SetActive(isHost);
            }

            // Send the saved name once the lobby object has spawned on this peer.
            if (!m_LobbyNameSent && lobby != null && lobby.IsSpawned)
            {
                m_LobbyNameSent = true;
                string saved = LoadSavedPlayerName();
                if (!string.IsNullOrEmpty(saved)) lobby.SetNameServerRpc(saved);
            }

            if (lobby == null || p == null) return;

            // Player cards are instantiated, so only rebuild when replicated lobby state actually changes.
            int hash = ComputeLobbyHash(lobby);
            if (m_LobbyDisplayValid && hash == m_LobbyDisplayHash) return;
            m_LobbyDisplayHash = hash;
            m_LobbyDisplayValid = true;

            string sceneName = lobby.selectedMapScene.Value.ToString();
            var mapData = mapRegistry != null ? mapRegistry.GetMapBySceneName(sceneName) : null;
            string mapLabel = mapData != null && mapData.sceneName == sceneName ? mapData.displayName : sceneName;
            int teamSize = Mathf.Max(1, lobby.targetTeamSize.Value);
            bool bots = lobby.enableBotFilling.Value;
            string status = lobby.autoStartCountdown.Value >= 0
                ? $"MATCH STARTS IN {lobby.autoStartCountdown.Value}s"
                : isHost ? "Start the battle when your team is ready." : "Waiting for the host to start the battle.";
            p.SetLobbyInfo($"LOBBY: <color=#{ColorUtility.ToHtmlStringRGB(AccentColor())}>{lobby.sessionName.Value.ToString().ToUpper()}</color>", status);
            p.SetMatchDetails(mapLabel, mapData != null ? mapData.previewImage : null,
                mapData != null ? mapData.themeColor : new Color(0.2f, 0.6f, 1f),
                lobby.reinforcementTickets.Value,
                bots ? $"{teamSize} (bots {lobby.botDifficulty.Value})" : $"{teamSize} (no bots)");
            if (m_HasShareInfo) p.SetJoinInfo(m_ShareLabel, m_ShareShown, m_ShareCopy);
            else p.SetJoinInfo("", "", "");

            if (p.redTeamContainer == null || p.blueTeamContainer == null) return;
            p.ClearPlayerCards();

            if (lobby.lobbyPlayers == null) return;
            ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
            int red = 0, blue = 0;
            for (int i = 0; i < lobby.lobbyPlayers.Count; i++)
            {
                var player = lobby.lobbyPlayers[i];
                if (player.team != Team.Red && player.team != Team.Blue) continue;
                if (player.team == Team.Red) red++; else blue++;

                string className = classRoster != null ? classRoster.NameOf(player.classIndex) : "";
                bool isLocal = player.clientId == localClientId;
                // Only your own card's ready toggle is clickable; the server flips the state.
                System.Action onReadyToggled = null;
                if (isLocal) onReadyToggled = () => ConquestLobbyManager.Instance?.ToggleReadyServerRpc();
                p.AddPlayerCard(player.playerName.ToString(), className, player.isHost, isLocal, player.isReady, player.team, onReadyToggled);
            }
            // Slots nobody holds yet: bots take them at the start when bot filling is on.
            string emptyLabel = bots ? "OPEN SLOT  (BOT)" : "OPEN SLOT";
            p.AddEmptySlots(Team.Red, teamSize - red, emptyLabel);
            p.AddEmptySlots(Team.Blue, teamSize - blue, emptyLabel);
            p.SetTeamCounts(red, blue, teamSize);
        }

        private Color AccentColor()
        {
            var theme = lobbyPanelUI != null ? lobbyPanelUI.theme : null;
            return theme != null ? theme.accent : new Color(0.35f, 0.78f, 1f);
        }

        private int ComputeLobbyHash(ConquestLobbyManager lobby)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + lobby.sessionName.Value.GetHashCode();
                h = h * 31 + lobby.selectedMapScene.Value.GetHashCode();
                h = h * 31 + lobby.reinforcementTickets.Value.GetHashCode();
                h = h * 31 + lobby.targetTeamSize.Value.GetHashCode();
                h = h * 31 + (int)lobby.botDifficulty.Value;
                h = h * 31 + lobby.autoStartCountdown.Value;
                h = h * 31 + (lobby.enableBotFilling.Value ? 1 : 0);
                if (lobby.lobbyPlayers != null)
                {
                    h = h * 31 + lobby.lobbyPlayers.Count;
                    for (int i = 0; i < lobby.lobbyPlayers.Count; i++)
                    {
                        var pl = lobby.lobbyPlayers[i];
                        h = h * 31 + pl.clientId.GetHashCode();
                        h = h * 31 + pl.playerName.GetHashCode();
                        h = h * 31 + (int)pl.team;
                        h = h * 31 + pl.classIndex;
                        h = h * 31 + (pl.isReady ? 1 : 0);
                        h = h * 31 + (pl.isHost ? 1 : 0);
                    }
                }
                return h;
            }
        }

        // ---------- Direct connect ----------

        private void SubscribeNetworkCallbacks()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm == m_SubscribedNetworkManager) return;

            UnsubscribeNetworkCallbacks();
            nm.OnClientDisconnectCallback += HandleNetworkDisconnect;
            nm.OnTransportFailure += HandleTransportFailure;
            m_SubscribedNetworkManager = nm;
        }

        private void UnsubscribeNetworkCallbacks()
        {
            if (m_SubscribedNetworkManager != null)
            {
                m_SubscribedNetworkManager.OnClientDisconnectCallback -= HandleNetworkDisconnect;
                m_SubscribedNetworkManager.OnTransportFailure -= HandleTransportFailure;
            }
            m_SubscribedNetworkManager = null;
        }

        private void HandleNetworkDisconnect(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            // On a host this fires for remote clients leaving; the lobby manager handles that.
            if (nm == null || nm.IsServer) return;

            bool wasConnecting = m_IsConnecting;
            m_IsConnecting = false;

            if (m_LeavingIntentionally)
            {
                m_LeavingIntentionally = false;
                return;
            }

            string reason = nm.DisconnectReason;
            if (string.IsNullOrEmpty(reason))
            {
                reason = wasConnecting
                    ? "Could not reach the server (timed out or refused)."
                    : "Disconnected from the server.";
            }
            ShowJoinError(reason);
        }

        private void HandleTransportFailure()
        {
            m_IsConnecting = false;
            m_LeavingIntentionally = false;
            ShowJoinError("Network transport failed. Check the address and try again.");
        }

        private void ShowJoinError(string message)
        {
            Debug.Log($"[ConquestMainMenuController] Connection ended: {message}");
            if (currentMenuState == MenuState.Lobby || currentMenuState == MenuState.ServerBrowser)
            {
                SwitchScreen(MenuState.ServerBrowser);
            }
            SetJoinStatus(message);
        }

        // The browser has no server discovery yet; its card list shows recently joined servers.
        private void RefreshBrowserPanel()
        {
            LoadRecentServers();
            var p = browserPanelUI;
            if (p != null && p.serverCardContainer != null)
            {
                p.ClearServerCards();
                if (p.serverCardPrefab != null)
                {
                    for (int i = 0; i < m_RecentServers.Count; i++)
                    {
                        int slot = i;
                        p.AddServerCard(m_RecentServers[i], "Recent server", () => ConnectToRecent(slot));
                    }
                }
            }
            UpdateJoinButtons();
        }

        private void UpdateJoinButtons()
        {
            var p = browserPanelUI;
            if (p == null) return;
            if (p.btn_Connect != null) p.btn_Connect.interactable = !m_IsConnecting;
            if (p.btn_ConnectDirect != null) p.btn_ConnectDirect.interactable = !m_IsConnecting;
            if (p.btn_Host != null) p.btn_Host.interactable = !m_IsConnecting;
            if (p.btn_Refresh != null) p.btn_Refresh.interactable = !m_IsConnecting;
        }

        // Join feedback goes to the header status badge (the browser panel has no status line).
        private void SetJoinStatus(string message)
        {
            if (!string.IsNullOrEmpty(message)) SetStatusBadge(message);
            UpdateJoinButtons();
        }

        private void ConnectToEnteredAddress()
        {
            var input = browserPanelUI != null ? browserPanelUI.tmp_DirectAddress : null;
            // Address field accepts "host" or "host:port"; BeginConnect splits it and defaults the port.
            BeginConnect(input != null ? input.text : "", "");
        }

        private void ConnectToRecent(int slot)
        {
            if (slot < 0 || slot >= m_RecentServers.Count) return;
            string entry = m_RecentServers[slot];
            var input = browserPanelUI != null ? browserPanelUI.tmp_DirectAddress : null;
            if (input != null) input.text = entry;
            BeginConnect(entry, "");
        }

        private void BeginConnect(string address, string portText)
        {
            if (m_IsConnecting) return;

            address = address != null ? address.Trim() : "";

            // A six-character code joins an online match through Relay; anything else is a LAN address.
            if (ConquestOnlineServices.LooksLikeJoinCode(address))
            {
                string code = ConquestOnlineServices.NormalizeJoinCode(address);
                m_LeavingIntentionally = false;
                var online = ConquestSession.Current;
                if (online == null || !online.StartClientConnect(ConquestConnectionData.ForJoinCode(code)))
                {
                    SetJoinStatus("Could not start the connection.");
                    return;
                }
                // Join codes expire with their match, so they are not kept as recent servers.
                m_IsConnecting = true;
                SetJoinStatus($"Joining online match {code}... (Back to cancel)");
                return;
            }

            // Accept "host:port" pasted into the address field.
            int colon = address.LastIndexOf(':');
            if (colon > 0 && address.IndexOf(':') == colon)
            {
                portText = address.Substring(colon + 1);
                address = address.Substring(0, colon);
            }

            if (string.IsNullOrEmpty(address))
            {
                SetJoinStatus("Enter a join code, or the host's IP address on your LAN.");
                return;
            }

            ushort port = ConquestSessionManager.DefaultPort;
            if (!string.IsNullOrWhiteSpace(portText) && (!ushort.TryParse(portText.Trim(), out port) || port == 0))
            {
                SetJoinStatus("Port must be a number between 1 and 65535.");
                return;
            }

            m_LeavingIntentionally = false;
            var session = ConquestSession.Current;
            if (session == null || !session.StartClientConnect(new ConquestConnectionData(address, port)))
            {
                SetJoinStatus("Could not start the connection.");
                return;
            }

            m_IsConnecting = true;
            RememberRecentServer($"{address}:{port}");
            SetJoinStatus($"Connecting to {address}:{port}... (Back to cancel)");
        }

        private void CancelConnect()
        {
            if (!m_IsConnecting) return;
            m_IsConnecting = false;
            m_LeavingIntentionally = true;
            ConquestSession.Current?.CancelConnect();
            SetJoinStatus("Connection cancelled.");
        }

        private void LeaveLobby()
        {
            m_IsConnecting = false;
            m_LeavingIntentionally = true;
            ConquestSession.Current?.LeaveSession();
            SwitchScreen(MenuState.Title);
        }

        private void LoadRecentServers()
        {
            m_RecentServers.Clear();
            string raw = PlayerPrefs.GetString(RecentServersPrefsKey, "");
            if (string.IsNullOrEmpty(raw)) return;
            foreach (var entry in raw.Split('|'))
            {
                if (!string.IsNullOrWhiteSpace(entry) && m_RecentServers.Count < MaxRecentServers)
                {
                    m_RecentServers.Add(entry.Trim());
                }
            }
        }

        private void RememberRecentServer(string entry)
        {
            LoadRecentServers();
            m_RecentServers.Remove(entry);
            m_RecentServers.Insert(0, entry);
            while (m_RecentServers.Count > MaxRecentServers) m_RecentServers.RemoveAt(m_RecentServers.Count - 1);
            PlayerPrefs.SetString(RecentServersPrefsKey, string.Join("|", m_RecentServers));
            PlayerPrefs.Save();
        }

        private static string LoadSavedPlayerName()
        {
            return ConquestSessionManager.LoadLocalPlayerName();
        }

        private void SubmitLobbyName()
        {
            var input = lobbyPanelUI != null ? lobbyPanelUI.tmp_PlayerName : null;
            string clean = ConquestSessionManager.SanitizePlayerName(input != null ? input.text : "");
            if (string.IsNullOrEmpty(clean)) return;

            ConquestSessionManager.SaveLocalPlayerName(clean);
            if (input != null) input.text = clean;

            var lobby = ConquestLobbyManager.Instance;
            if (lobby != null && lobby.IsSpawned) lobby.SetNameServerRpc(clean);
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
