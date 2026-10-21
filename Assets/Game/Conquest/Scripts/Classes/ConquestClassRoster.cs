using System.Collections.Generic;
using UnityEngine;
using Game.Conquest.Gameplay;

namespace Game.Conquest.Classes
{
    /// <summary>
    /// The playable classes in order, and the two teams' factions. A class index everywhere (lobby,
    /// deploy screen, server, scoreboard) is a position in this list for both teams, so the player
    /// and bot prefabs, the lobby and the menus all reference this one asset. A team's faction
    /// decides how its weapons sustain fire (overheat or magazine); the class decides the numbers.
    /// </summary>
    [CreateAssetMenu(fileName = "ClassRoster", menuName = "Conquest/Class Roster")]
    public class ConquestClassRoster : ScriptableObject
    {
        [SerializeField] private List<ConquestClassData> classes = new List<ConquestClassData>();

        [Header("Teams")]
        [Tooltip("What sets each team apart (weapon model, bolt look). Both teams share the class list above.")]
        [SerializeField] private ConquestFactionData redFaction;
        [SerializeField] private ConquestFactionData blueFaction;

        [Header("Bolts")]
        [SerializeField] private ConquestBoltSettings boltSettings;

        /// <summary>The bolt settings asset, or built-in defaults when none is assigned. Never null.</summary>
        public ConquestBoltSettings BoltSettings => boltSettings != null ? boltSettings : ConquestBoltSettings.Fallback;

        /// <summary>The faction a team plays as; null for Neutral (lobby) or a missing asset.</summary>
        public ConquestFactionData FactionFor(Team team)
        {
            if (team == Team.Red) return redFaction;
            if (team == Team.Blue) return blueFaction;
            return null;
        }

        public IReadOnlyList<ConquestClassData> Classes => classes;
        public int Count => classes.Count;

        /// <summary>The class at an index, or null when it is out of range.</summary>
        public ConquestClassData Get(int index)
        {
            return index >= 0 && index < classes.Count ? classes[index] : null;
        }

        public int ClampIndex(int index)
        {
            return Mathf.Clamp(index, 0, Mathf.Max(0, classes.Count - 1));
        }

        /// <summary>Display name for an index; "Unknown" if the index or the asset is missing.</summary>
        public string NameOf(int index)
        {
            var data = Get(index);
            return data != null && !string.IsNullOrEmpty(data.className) ? data.className : "Unknown";
        }
    }
}
