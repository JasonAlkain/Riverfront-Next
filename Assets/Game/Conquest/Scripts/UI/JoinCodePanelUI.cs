using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>Join a match by typing its join code (online) or the host's address (LAN).</summary>
    public class JoinCodePanelUI : MonoBehaviour
    {
        public TMP_InputField tmp_Address;
        public Button btn_Connect;
        public Button btn_Back;

        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
    }
}
