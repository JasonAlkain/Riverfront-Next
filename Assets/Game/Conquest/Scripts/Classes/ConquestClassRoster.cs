using System.Collections.Generic;
using UnityEngine;

namespace Game.Conquest.Classes
{
    /// <summary>
    /// The playable classes in order. A class index everywhere (lobby, deploy screen, server,
    /// scoreboard) is a position in this list, so the player and bot prefabs, the lobby and the
    /// menus all reference this one asset instead of keeping their own lists.
    /// </summary>
    [CreateAssetMenu(fileName = "ClassRoster", menuName = "Conquest/Class Roster")]
    public class ConquestClassRoster : ScriptableObject
    {
        [SerializeField] private List<ConquestClassData> classes = new List<ConquestClassData>();

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
