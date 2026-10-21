using TMPro;
using UnityEngine;

namespace Game.Conquest
{
    public class TicketBarUI : MonoBehaviour
    {
        [Header("Text Elements")]
        public TextMeshProUGUI txt_RedTickets;
        public TextMeshProUGUI txt_BlueTickets;

        [Header("Team Icons")]
        public UnityEngine.UI.Image img_RedIcon;
        public UnityEngine.UI.Image img_BlueIcon;

        private Color m_RedColor;
        private Color m_BlueColor;

        private int m_Red = -1;
        private int m_Blue = -1;

        // Only rewrites the text when a count changes, so the HUD doesn't allocate strings every frame.
        public void SetTickets(int red, int blue)
        {
            if (red != m_Red && txt_RedTickets != null) txt_RedTickets.text = red.ToString();
            if (blue != m_Blue && txt_BlueTickets != null) txt_BlueTickets.text = blue.ToString();
            m_Red = red;
            m_Blue = blue;
        }

        // Follows the colour-blind palette; only touches the graphics when a colour changes.
        public void SetTeamColors(Color red, Color blue)
        {
            if (red != m_RedColor)
            {
                m_RedColor = red;
                if (txt_RedTickets != null) txt_RedTickets.color = red;
                if (img_RedIcon != null) img_RedIcon.color = red;
            }
            if (blue != m_BlueColor)
            {
                m_BlueColor = blue;
                if (txt_BlueTickets != null) txt_BlueTickets.color = blue;
                if (img_BlueIcon != null) img_BlueIcon.color = blue;
            }
        }

        // Show and hide the ticket bar
        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
    }
}
