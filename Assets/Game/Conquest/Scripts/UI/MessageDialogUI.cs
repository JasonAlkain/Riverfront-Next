using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>A modal message over the menu: a disconnect, a failed join, any error worth reading.</summary>
    public class MessageDialogUI : MonoBehaviour
    {
        public TextMeshProUGUI txt_Title;
        public TextMeshProUGUI txt_Message;
        public Button btn_Ok;

        private void Awake()
        {
            if (btn_Ok != null) btn_Ok.onClick.AddListener(Hide);
        }

        public void ShowMessage(string title, string message)
        {
            if (txt_Title != null) txt_Title.text = title;
            if (txt_Message != null) txt_Message.text = message;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            if (btn_Ok != null) btn_Ok.Select();
        }

        public bool IsShown => gameObject.activeSelf;
        public void Hide() => gameObject.SetActive(false);
    }
}
