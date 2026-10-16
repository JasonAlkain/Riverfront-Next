using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    public class BrowserPanelUI : MonoBehaviour
    {
        [Header("Buttons")]
        public Button btn_Connect;
        public Button btn_Refresh;
        public Button btn_Back;
        public Button btn_Host;

        [Header("Direct IP")]
        public TMP_InputField tmp_DirectAddress;
        public Button btn_ConnectDirect;

        [Header("Server Cards")]
        public GameObject serverCardPrefab;
        public Transform serverCardContainer;

        public void AddServerCard(string serverName, string serverInfo, UnityEngine.Events.UnityAction onClickAction)
        {
            GameObject newCard = Instantiate(serverCardPrefab, serverCardContainer);
            ServerCardUI cardUI = newCard.GetComponent<ServerCardUI>();
            cardUI.SetServerName(serverName, serverInfo);
            cardUI.btn_Select.onClick.AddListener(onClickAction);
        }

        public void ClearServerCards()
        {
            foreach (Transform child in serverCardContainer)
            {
                Destroy(child.gameObject);
            }
        }
        
        public void Show()
        {
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
