using TMPro;
using UnityEngine;

namespace Game.Conquest
{
    public class MatchResultPanelUI : MonoBehaviour
    {
        [Header("Text Elements")]
        public TextMeshProUGUI txt_Title;
        public TextMeshProUGUI txt_Subtitle;

        public void SetResult(string title, Color titleColor, string subtitle)
        {
            if (txt_Title != null)
            {
                txt_Title.text = title;
                txt_Title.color = titleColor;
            }
            if (txt_Subtitle != null) txt_Subtitle.text = subtitle;
        }

        // Show and hide the match result panel
        public void Show() { if (!gameObject.activeSelf) gameObject.SetActive(true); }
        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }
    }
}
