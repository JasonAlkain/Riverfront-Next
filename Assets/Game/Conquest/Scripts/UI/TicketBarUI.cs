using TMPro;
using UnityEngine;

namespace Game.Conquest
{
    public class TicketBarUI : MonoBehaviour
    {
        [Header("Text Elements")]
        public TextMeshProUGUI txt_RedTickets;
        public TextMeshProUGUI txt_BlueTickets;

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

        // Show and hide the ticket bar
        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
    }
}
