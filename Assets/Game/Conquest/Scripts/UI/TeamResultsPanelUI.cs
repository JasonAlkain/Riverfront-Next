using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>A team's column on the results screen: header plus one row per combatant.</summary>
    public class TeamResultsPanelUI : MonoBehaviour
    {
        [Header("Header")]
        public TextMeshProUGUI txt_TeamName;
        public TextMeshProUGUI txt_TeamTotals;
        public Image img_Header;

        [Header("Rows")]
        public Transform rowsContainer;
        public MatchResultRowUI rowPrefab;

        public void SetHeader(string teamName, string totals, Color teamColor)
        {
            if (txt_TeamName != null) txt_TeamName.text = teamName;
            if (txt_TeamTotals != null) txt_TeamTotals.text = totals;
            if (img_Header != null) img_Header.color = teamColor;
        }

        public void ClearRows()
        {
            if (rowsContainer == null) return;
            for (int i = rowsContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(rowsContainer.GetChild(i).gameObject);
            }
        }

        public void AddRow(string playerName, string className, int kills, int deaths, Color background)
        {
            if (rowPrefab == null || rowsContainer == null)
            {
                Debug.LogError("[TeamResultsPanelUI] Row prefab or container is not assigned.", this);
                return;
            }
            var row = Instantiate(rowPrefab, rowsContainer);
            row.SetRow(playerName, className, kills, deaths, background);
        }
    }
}
