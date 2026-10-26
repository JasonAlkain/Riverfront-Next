using UnityEngine;

namespace Game.Conquest
{
    /// <summary>The short "latest update" list on the title screen, and where the full devlog lives.</summary>
    [CreateAssetMenu(fileName = "Changelog", menuName = "Conquest/Changelog")]
    public class ChangelogData : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            public string version = "Pre-Alpha";
            [Tooltip("Not shown on the title screen, which shows the build version instead. Kept as a record.")]
            public string date = "";
            [TextArea] public string[] changes = new string[0];
        }

        [Tooltip("Opened by the title screen's devlog button. Only https links open.")]
        public string devlogUrl = "https://allyn-david.itch.io/riverfront-next/devlog";
        [Tooltip("Newest first. The title screen shows the first entry.")]
        public Entry[] entries = new Entry[0];
    }
}
