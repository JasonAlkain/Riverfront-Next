using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    public class PlayerStatsUI : MonoBehaviour
    {
        [Header("Images")]
        public Image img_Background;

        [Header("Text Elements")]
        public TextMeshProUGUI txt_TeamClass;
        public TextMeshProUGUI txt_Health;
        public TextMeshProUGUI txt_Ammo;

        public void SetTeamColor(Color color)
        {
            if (img_Background != null) img_Background.color = color;
        }

        public void SetStats(string teamClass, string health, string ammo)
        {
            if (txt_TeamClass != null) txt_TeamClass.text = teamClass;
            if (txt_Health != null) txt_Health.text = health;
            if (txt_Ammo != null) txt_Ammo.text = ammo;
        }

        // Show and hide the stats card
        public void Show() { if (!gameObject.activeSelf) gameObject.SetActive(true); }
        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }
    }
}
