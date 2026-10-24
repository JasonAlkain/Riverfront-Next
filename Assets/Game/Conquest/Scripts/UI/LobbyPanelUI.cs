using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Game.Conquest.Gameplay;
using Game.Conquest.Settings;
using Game.Conquest.UI;

namespace Game.Conquest
{
    public class LobbyPanelUI : MonoBehaviour
    {

        // Lobby information
        public TextMeshProUGUI txt_LobbyName;
        [Tooltip("Status line under the teams: the match countdown, or what the lobby is waiting for.")]
        public TextMeshProUGUI txt_LobbyInfo;

        [Header("Match details")]
        public TextMeshProUGUI txt_MapName;
        public TextMeshProUGUI txt_Tickets;
        public TextMeshProUGUI txt_TeamSize;
        [Tooltip("Map preview; tinted with the map's colour when it has no preview image.")]
        public Image img_MapPreview;
        public TextMeshProUGUI txt_MapPreviewLabel;

        [Header("Join code")]
        [Tooltip("Box with the join code (or LAN address) and its copy button. Hidden when there's nothing to share.")]
        public GameObject joinBox;
        public TextMeshProUGUI txt_JoinLabel;
        public TextMeshProUGUI txt_JoinCode;
        public Button btn_CopyJoinCode;
        public TextMeshProUGUI txt_CopyButtonLabel;
        [Tooltip("Placeholder: seconds the copy button says COPIED.")]
        public float copiedFeedbackSeconds = 1.5f;

        // Player Card Prefab
        public GameObject playerCardPrefab;
        [Tooltip("Placeholder row for a team slot nobody holds yet.")]
        public GameObject emptySlotPrefab;

        // Team information
        public Transform redTeamContainer;
        public Transform blueTeamContainer;
        public TextMeshProUGUI txt_RedCount;
        public TextMeshProUGUI txt_BlueCount;
        [Tooltip("Team-coloured parts of each team panel (header band, edge), tinted from the theme.")]
        public Graphic[] redTeamTinted;
        public Graphic[] blueTeamTinted;
        public ConquestUITheme theme;

        // Buttons
        public Button btn_SwitchTeam;
        public Button btn_LeaveLobby;

        // Optional: wired by ConquestMainMenuController when assigned
        public Button btn_ToggleReady;
        public Button btn_StartMatch;   // host only
        public Button btn_CycleMap;     // host only
        public TMP_InputField tmp_PlayerName;
        public Button btn_SetName;

        private string m_CopyText = "";
        private Coroutine m_CopiedRoutine;

        private void Awake()
        {
            if (btn_CopyJoinCode != null) btn_CopyJoinCode.onClick.AddListener(CopyJoinCode);
        }

        private void OnEnable()
        {
            ApplyTeamColors();
            if (txt_CopyButtonLabel != null) txt_CopyButtonLabel.text = "COPY";
        }

        // Set the lobby name and info
        public void SetLobbyInfo(string lobbyName, string lobbyInfo)
        {
            if (txt_LobbyName != null) txt_LobbyName.text = lobbyName;
            if (txt_LobbyInfo != null) txt_LobbyInfo.text = lobbyInfo;
        }

        public void SetMatchDetails(string mapName, Sprite preview, Color mapColor, int tickets, string teamSize)
        {
            if (txt_MapName != null) txt_MapName.text = mapName;
            if (txt_Tickets != null) txt_Tickets.text = tickets.ToString();
            if (txt_TeamSize != null) txt_TeamSize.text = teamSize;
            if (img_MapPreview != null)
            {
                img_MapPreview.sprite = preview;
                img_MapPreview.color = preview != null ? Color.white : new Color(mapColor.r * 0.45f, mapColor.g * 0.45f, mapColor.b * 0.45f, 1f);
            }
            if (txt_MapPreviewLabel != null)
            {
                txt_MapPreviewLabel.gameObject.SetActive(preview == null);
                txt_MapPreviewLabel.text = mapName.ToUpperInvariant();
            }
        }

        /// <summary>Shows what other players need to join and what the copy button copies. Empty hides the box.</summary>
        public void SetJoinInfo(string label, string shown, string copyText)
        {
            bool show = !string.IsNullOrEmpty(shown);
            if (joinBox != null && joinBox.activeSelf != show) joinBox.SetActive(show);
            if (!show) return;
            if (txt_JoinLabel != null) txt_JoinLabel.text = label;
            if (txt_JoinCode != null) txt_JoinCode.text = shown;
            m_CopyText = string.IsNullOrEmpty(copyText) ? shown : copyText;
        }

        public void SetTeamCounts(int red, int blue, int teamSize)
        {
            if (txt_RedCount != null) txt_RedCount.text = $"{red} / {teamSize} PLAYERS";
            if (txt_BlueCount != null) txt_BlueCount.text = $"{blue} / {teamSize} PLAYERS";
        }

        private void CopyJoinCode()
        {
            if (string.IsNullOrEmpty(m_CopyText)) return;
            GUIUtility.systemCopyBuffer = m_CopyText;
            if (m_CopiedRoutine != null) StopCoroutine(m_CopiedRoutine);
            m_CopiedRoutine = StartCoroutine(ShowCopied());
        }

        private IEnumerator ShowCopied()
        {
            if (txt_CopyButtonLabel != null) txt_CopyButtonLabel.text = "COPIED";
            yield return new WaitForSecondsRealtime(copiedFeedbackSeconds);
            if (txt_CopyButtonLabel != null) txt_CopyButtonLabel.text = "COPY";
            m_CopiedRoutine = null;
        }

        private void ApplyTeamColors()
        {
            if (theme == null) return;
            var settings = ConquestSettingsManager.Instance != null ? ConquestSettingsManager.Instance.CurrentSettings : null;
            bool colorBlind = settings != null && settings.colorBlindTeams;
            Tint(redTeamTinted, theme.TeamColor(Team.Red, colorBlind));
            Tint(blueTeamTinted, theme.TeamColor(Team.Blue, colorBlind));
        }

        // Each graphic keeps its own alpha, so a faint band and a solid edge can share one colour.
        private static void Tint(Graphic[] graphics, Color color)
        {
            if (graphics == null) return;
            foreach (var g in graphics)
            {
                if (g == null) continue;
                g.color = new Color(color.r, color.g, color.b, g.color.a);
            }
        }

        public Color TeamColor(Team team)
        {
            if (theme == null) return team == Team.Red ? new Color(0.85f, 0.2f, 0.2f) : new Color(0.2f, 0.5f, 0.9f);
            var settings = ConquestSettingsManager.Instance != null ? ConquestSettingsManager.Instance.CurrentSettings : null;
            return theme.TeamColor(team, settings != null && settings.colorBlindTeams);
        }

        //Add a player card to the appropriate team. Pass onReadyToggled for the local player's own card.
        public void AddPlayerCard(string playerName, bool isReady = false, bool isRedTeam = true, Action onReadyToggled = null)
        {
            AddPlayerCard(playerName, "", false, onReadyToggled != null, isReady, isRedTeam ? Team.Red : Team.Blue, onReadyToggled);
        }

        public void AddPlayerCard(string playerName, string className, bool isHost, bool isLocal, bool isReady, Team team, Action onReadyToggled)
        {
            if(playerCardPrefab == null)
            {
                Debug.LogError("Player card prefab is not assigned.");
                return;
            }
            if (redTeamContainer == null || blueTeamContainer == null)
            {
                Debug.LogError("Team containers are not assigned.");
                return;
            }
            if (string.IsNullOrEmpty(playerName))
            {
                Debug.LogError("Player name is null or empty.");
                return;
            }
            GameObject newCard = Instantiate(playerCardPrefab, team == Team.Red ? redTeamContainer : blueTeamContainer);
            PlayerCardUI cardUI = newCard.GetComponent<PlayerCardUI>();
            cardUI.SetPlayerInfo(playerName, className, isHost, isLocal, isReady, TeamColor(team), onReadyToggled);
        }

        /// <summary>Pads a team list with empty slots up to the team size.</summary>
        public void AddEmptySlots(Team team, int count, string label)
        {
            if (emptySlotPrefab == null || count <= 0) return;
            Transform container = team == Team.Red ? redTeamContainer : blueTeamContainer;
            if (container == null) return;
            for (int i = 0; i < count; i++)
            {
                GameObject slot = Instantiate(emptySlotPrefab, container);
                var text = slot.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
                if (text != null && !string.IsNullOrEmpty(label)) text.text = label;
            }
        }

        // Clear all player cards from both teams
        public void ClearPlayerCards()
        {
            foreach (Transform child in redTeamContainer)
            {
                Destroy(child.gameObject);
            }
            foreach (Transform child in blueTeamContainer)
            {
                Destroy(child.gameObject);
            }
        }

        // Show and hide the lobby panel
        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
    }
}
