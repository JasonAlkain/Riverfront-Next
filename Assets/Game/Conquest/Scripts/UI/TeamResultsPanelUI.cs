using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Game.Conquest.Classes;
using Game.Conquest.Gameplay;

namespace Game.Conquest
{
    /// <summary>
    /// A team's column: header plus one row per combatant. Used by the results screen and the
    /// in-match Tab scoreboard.
    /// </summary>
    public class TeamResultsPanelUI : MonoBehaviour
    {
        [Header("Header")]
        public TextMeshProUGUI txt_TeamName;
        public TextMeshProUGUI txt_TeamTotals;
        public Image img_Header;

        [Header("Rows")]
        public Transform rowsContainer;
        public MatchResultRowUI rowPrefab;

        private static readonly List<MatchResultRow> s_Sorted = new List<MatchResultRow>();

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

        /// <summary>Fill the column with one team's rows, best first, and the team's totals.</summary>
        public void ShowTeam(IEnumerable<MatchResultRow> rows, Team team, string teamName, Color teamColor, Color rowColor, Color botRowColor)
        {
            s_Sorted.Clear();
            int kills = 0, deaths = 0, captures = 0;
            foreach (var row in rows)
            {
                if (row.team != team) continue;
                s_Sorted.Add(row);
                kills += row.kills;
                deaths += row.deaths;
                captures += row.captures;
            }
            // Most kills first; fewer deaths breaks ties.
            s_Sorted.Sort((a, b) => a.kills != b.kills ? b.kills.CompareTo(a.kills) : a.deaths.CompareTo(b.deaths));

            SetHeader(teamName, $"Kills {kills}  Deaths {deaths}  Captures {captures}", teamColor);
            ClearRows();
            foreach (var row in s_Sorted)
            {
                string name = row.isBot ? $"{row.playerName} [BOT]" : row.playerName.ToString();
                AddRow(name, ClassLabel(row.classIndex), row.kills, row.deaths, row.isBot ? botRowColor : rowColor);
            }
        }

        private static string ClassLabel(int classIndex)
        {
            return System.Enum.IsDefined(typeof(InfantryClassType), classIndex)
                ? ((InfantryClassType)classIndex).ToString()
                : $"Class {classIndex}";
        }
    }
}
