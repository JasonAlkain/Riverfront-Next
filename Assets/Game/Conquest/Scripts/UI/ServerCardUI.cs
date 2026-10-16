using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game.Conquest
{
    public class ServerCardUI : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI serverNameText;
        public Button btn_Select;


        public void SetServerName(string serverName, string serverInfo)
        {
            serverNameText.text = $"<b>{serverName}</b>" + "\n" + serverInfo;
        }
    }
}
