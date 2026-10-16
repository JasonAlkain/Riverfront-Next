using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>One combatant's line in a team's results list.</summary>
    public class MatchResultRowUI : MonoBehaviour
    {
        [Header("Text Elements")]
        public TextMeshProUGUI txt_Name;
        public TextMeshProUGUI txt_Class;
        public TextMeshProUGUI txt_Kills;
        public TextMeshProUGUI txt_Deaths;

        [Header("Background")]
        public Image img_Background;

        public void SetRow(string playerName, string className, int kills, int deaths, Color background)
        {
            if (txt_Name != null) txt_Name.text = playerName;
            if (txt_Class != null) txt_Class.text = className;
            if (txt_Kills != null) txt_Kills.text = kills.ToString();
            if (txt_Deaths != null) txt_Deaths.text = deaths.ToString();
            if (img_Background != null) img_Background.color = background;
        }
    }
}
