using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>Shows the newest changelog entry and opens the devlog page in the browser.</summary>
    public class ChangelogUI : MonoBehaviour
    {
        public ChangelogData data;
        public TextMeshProUGUI txt_Headline;
        public TextMeshProUGUI txt_Changes;
        public Button btn_Devlog;
        [Tooltip("Placeholder: how many lines of the newest entry fit in the box.")]
        public int maxLines = 4;

        private void Awake()
        {
            if (btn_Devlog != null) btn_Devlog.onClick.AddListener(OpenDevlog);
        }

        private void OnEnable()
        {
            if (data == null || data.entries == null || data.entries.Length == 0) return;
            var entry = data.entries[0];
            if (txt_Headline != null)
            {
                txt_Headline.text = string.IsNullOrEmpty(entry.date) ? entry.version : $"{entry.version}  ·  {entry.date}";
            }
            if (txt_Changes != null)
            {
                var sb = new StringBuilder();
                int count = Mathf.Min(maxLines, entry.changes != null ? entry.changes.Length : 0);
                for (int i = 0; i < count; i++)
                {
                    if (i > 0) sb.Append('\n');
                    sb.Append("•  ").Append(entry.changes[i]);
                }
                txt_Changes.text = sb.ToString();
            }
            if (btn_Devlog != null) btn_Devlog.interactable = IsOpenable(data.devlogUrl);
        }

        private void OpenDevlog()
        {
            if (data != null && IsOpenable(data.devlogUrl)) Application.OpenURL(data.devlogUrl);
        }

        private static bool IsOpenable(string url) => !string.IsNullOrEmpty(url) && url.StartsWith("https://");
    }
}
