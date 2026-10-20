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
        JoinByCode, // Kept so serialized indices stay stable; routed to ServerBrowser (direct IP lives there).
        Lobby,
        Settings,
        Credits
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

        [Header("Active Screen")]
        [SerializeField] private MenuState currentMenuState = MenuState.Title;

        [Header("Panel UI References")]
        [SerializeField] private HeaderBannerUI headerBannerUI;
        [SerializeField] private TitlePanelUI titlePanelUI;
        [SerializeField] private SoloPanelUI soloPanelUI;
        [SerializeField] private MultiplayerPanelUI multiplayerPanelUI;
        [SerializeField] private HostPanelUI hostPanelUI;
        [SerializeField] private BrowserPanelUI browserPanelUI;
        //[SerializeField] private JoinCodePanelUI joinCodePanelUI;
        [SerializeField] private LobbyPanelUI lobbyPanelUI;
        [SerializeField] private SettingsPanelUI settingsPanelUI;
        [SerializeField] private CreditsPanelUI creditsPanelUI;

        // Color fields are no longer applied in code (the prefab owns visuals). They stay serialized
        // so the values already saved in MainMenu.unity are preserved for the planned button theming.
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

        // Direct connect state
        private readonly List<string> m_RecentServers = new List<string>();
        private bool m_IsConnecting = false;
        private bool m_LeavingIntentionally = false;
        private NetworkManager m_SubscribedNetworkManager;

        private const int MaxRecentServers = 3;
        private const string RecentServersPrefsKey = "Conquest.RecentServers";

        // Lobby state
        private string m_LobbyJoinAddressLine = "";
        private bool m_LobbyNameSent = false;
        private int m_LobbyDisplayHash;
        private bool m_LobbyDisplayValid;

        private static readonly string[] k_ClassNames = { "Assault", "Heavy", "Engineer", "Recon" };

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

            BindTitlePanel();
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
            }
            if (Instance == this) Instance = null;
        }

        private void HandleStatusChanged(string msg)
        {
            SetStatusBadge(msg);
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
            currentMenuState = newState;

            SetPanelVisible(titlePanelUI, newState == MenuState.Title);
            SetPanelVisible(soloPanelUI, newState == MenuState.PlaySolo);
            SetPanelVisible(multiplayerPanelUI, newState == MenuState.Multiplayer);
            SetPanelVisible(hostPanelUI, newState == MenuState.HostGame);
            SetPanelVisible(browserPanelUI, newState == MenuState.ServerBrowser);
            SetPanelVisible(lobbyPanelUI, newState == MenuState.Lobby);
            SetPanelVisible(settingsPanelUI, newState == MenuState.Settings);
            SetPanelVisible(creditsPanelUI, newState == MenuState.Credits);

            if (newState == MenuState.PlaySolo) UpdateSoloDisplay();
            if (newState == MenuState.HostGame) UpdateHostDisplay();
            if (newState == MenuState.ServerBrowser) RefreshBrowserPanel();
            if (newState == MenuState.Settings) LoadSettingsIntoUI();
            if (newState == MenuState.Lobby)
            {
                // Enumerating interfaces allocates, so resolve the join address once per lobby visit.
                bool hosting = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
                m_LobbyJoinAddressLine = hosting && ConquestSession.Current != null
                    ? $"\nPLAYERS JOIN AT: {ConquestSession.Current.HostJoinAddress}"
                    : "";
                m_LobbyNameSent = false;
                m_LobbyDisplayValid = false;
                if (lobbyPanelUI != null && lobbyPanelUI.tmp_PlayerName != null)
                {
                    lobbyPanelUI.tmp_PlayerName.text = LoadSavedPlayerName();
                }
                UpdateLobbyDisplay();
            }
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
            Bind(titlePanelUI.btn_Singleplayer, () => SwitchScreen(MenuState.PlaySolo));
            Bind(titlePanelUI.btn_Multiplayer, () => SwitchScreen(MenuState.Multiplayer));
            Bind(titlePanelUI.btn_Settings, () => SwitchScreen(MenuState.Settings));
            Bind(titlePanelUI.btn_Credits, () => SwitchScreen(MenuState.Credits));
            Bind(titlePanelUI.btn_Quit, QuitApplication);
        }

        private void BindSoloPanel()
        {
            var p = soloPanelUI;
            if (p == null) return;

            Bind(p.btn_PrevMap, () => CycleMap(-1));
            Bind(p.btn_NextMap, () => CycleMap(1));
            Bind(p.btn_Start, LaunchSoloGame);
            Bind(p.btn_Back, () => SwitchScreen(MenuState.Title));

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

            if (p.btn_Classes != null)
            {
                for (int i = 0; i < p.btn_Classes.Length; i++)
                {
                    int classIndex = i;
                    Bind(p.btn_Classes[i], () => ConquestLobbyManager.Instance?.SelectClassServerRpc(classIndex));
                }
            }

            if (p.tmp_PlayerName != null)
            {
                p.tmp_PlayerName.characterLimit = ConquestSessionManager.MaxPlayerNameLength;
                p.tmp_PlayerName.text = LoadSavedPlayerName();
            }
        }

        private void BindSettingsPanel()
        {
            var p = settingsPanelUI;
            if (p == null) return;

            BindSlider(p.masterVolume, false, 0.85f, _ => RefreshSettingsLabels());
            BindSlider(p.sfxVolume, false, 0.80f, _ => RefreshSettingsLabels());
            BindSlider(p.musicVolume, false, 0.70f, _ => RefreshSettingsLabels());
            BindSlider(p.mouseSensitivity, false, 0.15f, _ => RefreshSettingsLabels());
            BindSlider(p.fovSlider, false, 75f, _ => RefreshSettingsLabels());
            RefreshSettingsLabels();

            Bind(p.btn_Apply, SaveAndApplySettings);
            Bind(p.btn_Defaults, RestoreSettingsDefaults);
            Bind(p.btn_Back, () => SwitchScreen(MenuState.Title));
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
            if (soloPanelUI != null) SetText(soloPanelUI.txt_Difficulty, $"Bot Difficulty: {(BotDifficulty)diff}");
        }

        private void UpdateSoloLabels()
        {
            if (soloPanelUI == null) return;
            SetText(soloPanelUI.txt_TeamSize, $"Target Team Size: {m_SoloTeamSize} ({m_SoloTeamSize * 2} combatants)");
            SetText(soloPanelUI.txt_Tickets, $"Reinforcement Tickets: {m_SoloTickets}");
        }

        private void UpdateHostLabels()
        {
            if (hostPanelUI == null) return;
            SetText(hostPanelUI.txt_MaxHumanConnections, $"Max Human Connections: {m_HostMaxPlayers}");
            SetText(hostPanelUI.txt_ReinforcementTickets, $"Reinforcement Tickets: {m_HostTickets}");
            SetText(hostPanelUI.txt_TargetTeamSize, $"Target Team Size: {m_HostTeamSize} (AI Filled)");
        }

        private void UpdateSoloDisplay()
        {
            if (soloPanelUI == null) return;
            var maps = mapRegistry != null ? mapRegistry.RegisteredMaps : null;
            if (maps != null && maps.Count > 0)
            {
                m_SelectedMapIndex = Mathf.Clamp(m_SelectedMapIndex, 0, maps.Count - 1);
                var map = maps[m_SelectedMapIndex];
                SetText(soloPanelUI.txt_MapName, $"<b>{map.displayName.ToUpper()}</b> ({map.commandPostsCount} CPs)");
                SetText(soloPanelUI.txt_MapDescription, $"{map.description}\nRecommended: {map.recommendedPlayers} Players  |  Scene: {map.sceneName}");
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
                SetText(hostPanelUI.txt_SelectedMap, $"Selected Map: <b>{map.displayName}</b> ({map.commandPostsCount} Posts)");
            }
        }

        private void LaunchSoloGame()
        {
            var maps = mapRegistry != null ? mapRegistry.RegisteredMaps : null;
            var map = maps != null && maps.Count > 0 ? maps[m_SelectedMapIndex] : null;
            string sceneName = map != null ? map.sceneName : "ConquestLargeBattlefield";
            ConquestSession.Current?.StartSoloGame(sceneName, m_SoloTickets, m_SoloTeamSize, (BotDifficulty)m_SoloBotDifficultyIndex);
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
            bool success = session != null && session.StartHostGame(
                mapId, sceneName, sName, m_HostMaxPlayers, m_HostTickets, m_HostTeamSize, m_HostBotFilling, (BotDifficulty)m_HostBotDifficultyIndex);

            if (success)
            {
                SwitchScreen(MenuState.Lobby);
            }
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
            if (p.txt_LobbyName != null && p.txt_LobbyInfo != null)
            {
                string countdownLine = lobby.autoStartCountdown.Value >= 0 ? $"\nMATCH STARTS IN {lobby.autoStartCountdown.Value}s" : "";
                p.SetLobbyInfo(
                    $"LOBBY: {lobby.sessionName.Value.ToString().ToUpper()}",
                    $"MAP: {mapLabel} | TICKETS: {lobby.reinforcementTickets.Value} | TEAM SIZE: {lobby.targetTeamSize.Value} (bots {lobby.botDifficulty.Value}){m_LobbyJoinAddressLine}{countdownLine}");
            }

            if (p.redTeamContainer == null || p.blueTeamContainer == null) return;
            p.ClearPlayerCards();

            if (lobby.lobbyPlayers == null) return;
            ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
            for (int i = 0; i < lobby.lobbyPlayers.Count; i++)
            {
                var player = lobby.lobbyPlayers[i];
                if (player.team != Team.Red && player.team != Team.Blue) continue;

                string hostTag = player.isHost ? " (Host)" : "";
                string className = player.classIndex >= 0 && player.classIndex < k_ClassNames.Length ? k_ClassNames[player.classIndex] : $"Class {player.classIndex}";
                // Only your own card's ready toggle is clickable; the server flips the state.
                System.Action onReadyToggled = null;
                if (player.clientId == localClientId) onReadyToggled = () => ConquestLobbyManager.Instance?.ToggleReadyServerRpc();
                p.AddPlayerCard($"{player.playerName}{hostTag} - {className}", player.isReady, player.team == Team.Red, onReadyToggled);
            }
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
            // Accept "host:port" pasted into the address field.
            int colon = address.LastIndexOf(':');
            if (colon > 0 && address.IndexOf(':') == colon)
            {
                portText = address.Substring(colon + 1);
                address = address.Substring(0, colon);
            }

            if (string.IsNullOrEmpty(address))
            {
                SetJoinStatus("Enter the server's IP address.");
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

        private void LoadSettingsIntoUI()
        {
            var sm = ConquestSettingsManager.Instance;
            var p = settingsPanelUI;
            if (sm == null || p == null) return;
            var s = sm.CurrentSettings;

            if (p.masterVolume != null) p.masterVolume.SetValueWithoutNotify(s.masterVolume);
            if (p.sfxVolume != null) p.sfxVolume.SetValueWithoutNotify(s.sfxVolume);
            if (p.musicVolume != null) p.musicVolume.SetValueWithoutNotify(s.musicVolume);
            if (p.mouseSensitivity != null) p.mouseSensitivity.SetValueWithoutNotify(s.mouseSensitivity);
            if (p.fovSlider != null) p.fovSlider.SetValueWithoutNotify(s.fieldOfView);
            RefreshSettingsLabels();
        }

        private void RefreshSettingsLabels()
        {
            var p = settingsPanelUI;
            if (p == null) return;
            if (p.masterVolume != null) SetText(p.txt_MasterVolume, $"Master Volume: {(int)(p.masterVolume.value * 100)}%");
            if (p.sfxVolume != null) SetText(p.txt_SfxVolume, $"SFX Volume: {(int)(p.sfxVolume.value * 100)}%");
            if (p.musicVolume != null) SetText(p.txt_MusicVolume, $"Music Volume: {(int)(p.musicVolume.value * 100)}%");
            if (p.mouseSensitivity != null) SetText(p.txt_MouseSensitivity, $"Mouse Sensitivity: {p.mouseSensitivity.value:F2}");
            if (p.fovSlider != null) SetText(p.txt_Fov, $"Field of View: {(int)p.fovSlider.value}°");
        }

        private void SaveAndApplySettings()
        {
            var sm = ConquestSettingsManager.Instance;
            var p = settingsPanelUI;
            if (sm == null || p == null) return;
            var s = sm.CurrentSettings;

            if (p.masterVolume != null) s.masterVolume = p.masterVolume.value;
            if (p.sfxVolume != null) s.sfxVolume = p.sfxVolume.value;
            if (p.musicVolume != null) s.musicVolume = p.musicVolume.value;
            if (p.mouseSensitivity != null) s.mouseSensitivity = p.mouseSensitivity.value;
            if (p.fovSlider != null) s.fieldOfView = p.fovSlider.value;

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
