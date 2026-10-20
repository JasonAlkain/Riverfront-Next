using TMPro;
using UnityEngine;

namespace Game.Conquest
{
    /// <summary>Banner shown while one team holds every command post and is counting down to a win.</summary>
    public class AllPostsAlertUI : MonoBehaviour
    {
        [Header("Text Elements")]
        public TextMeshProUGUI txt_Message;

        private int m_Seconds = -1;
        private bool m_OurTeam;

        // Only rewrites the text when the whole seconds or the side change, so it doesn't allocate every frame.
        public void SetCountdown(int seconds, bool ourTeamHoldsAll, Color color)
        {
            if (txt_Message == null) return;
            if (seconds == m_Seconds && ourTeamHoldsAll == m_OurTeam) return;

            m_Seconds = seconds;
            m_OurTeam = ourTeamHoldsAll;
            txt_Message.color = color;
            txt_Message.text = ourTeamHoldsAll
                ? $"We hold every command post. Victory in {seconds}"
                : $"The enemy holds every command post! Retake one within {seconds}";
        }

        // Show and hide the alert
        public void Show() { if (!gameObject.activeSelf) gameObject.SetActive(true); }
        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
            m_Seconds = -1;
        }
    }
}
