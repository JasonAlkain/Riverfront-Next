using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>Top of the results screen: outcome, match facts and the return-to-lobby countdown.</summary>
    public class MatchSummaryPanelUI : MonoBehaviour
    {
        [Header("Text Elements")]
        public TextMeshProUGUI txt_Title;
        public TextMeshProUGUI txt_Subtitle;
        public TextMeshProUGUI txt_MatchInfo;
        public TextMeshProUGUI txt_Countdown;

        [Header("Buttons")]
        [Tooltip("Host only: skip the countdown and return everyone to the lobby now.")]
        public Button btn_ReturnNow;

        public void SetOutcome(string title, Color titleColor, string subtitle)
        {
            if (txt_Title != null)
            {
                txt_Title.text = title;
                txt_Title.color = titleColor;
            }
            if (txt_Subtitle != null) txt_Subtitle.text = subtitle;
        }

        public void SetMatchInfo(string info)
        {
            if (txt_MatchInfo != null) txt_MatchInfo.text = info;
        }

        public void SetCountdown(string text)
        {
            if (txt_Countdown != null) txt_Countdown.text = text;
        }

        public void SetReturnNowVisible(bool visible)
        {
            if (btn_ReturnNow != null) btn_ReturnNow.gameObject.SetActive(visible);
        }
    }
}
