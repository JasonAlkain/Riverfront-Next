using UnityEngine;

namespace Game.Conquest
{
    /// <summary>The in-match scoreboard shown while Tab is held: one column per team.</summary>
    public class ScoreboardUI : MonoBehaviour
    {
        [Header("Team Columns")]
        public TeamResultsPanelUI redTeamPanel;
        public TeamResultsPanelUI blueTeamPanel;

        public bool IsShown => gameObject.activeSelf;

        // Show and hide the scoreboard
        public void Show() { if (!gameObject.activeSelf) gameObject.SetActive(true); }
        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }
    }
}
