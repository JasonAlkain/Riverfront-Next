using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    public class LobbyPanelUI : MonoBehaviour
    {

        // Lobby information
        public TextMeshProUGUI txt_LobbyName;
        public TextMeshProUGUI txt_LobbyInfo;

        // Player Card Prefab
        public GameObject playerCardPrefab;

        // Team information
        public Transform redTeamContainer;
        public Transform blueTeamContainer;

        // Buttons
        public Button btn_SwitchTeam;
        public Button btn_LeaveLobby;

        // Optional: wired by ConquestMainMenuController when assigned
        public Button btn_ToggleReady;
        public Button btn_StartMatch;   // host only
        public Button btn_CycleMap;     // host only
        [Tooltip("Builds one button per class from the class list.")]
        public ClassPickerUI classPicker;
        public TMP_InputField tmp_PlayerName;
        public Button btn_SetName;


        // Set the lobby name and info
        public void SetLobbyInfo(string lobbyName, string lobbyInfo)
        {
            txt_LobbyName.text = $"<b>{lobbyName}</b>";
            txt_LobbyInfo.text = lobbyInfo;
        }

        //Add a player card to the appropriate team. Pass onReadyToggled for the local player's own card.
        public void AddPlayerCard(string playerName, bool isReady = false, bool isRedTeam = true, Action onReadyToggled = null)
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
            GameObject newCard = Instantiate(playerCardPrefab, isRedTeam ? redTeamContainer : blueTeamContainer);
            PlayerCardUI cardUI = newCard.GetComponent<PlayerCardUI>();
            cardUI.SetPlayerInfo(playerName, isReady, onReadyToggled);
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
