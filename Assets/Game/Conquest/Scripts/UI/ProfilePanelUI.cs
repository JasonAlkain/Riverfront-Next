using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Game.Conquest.Profile;

namespace Game.Conquest
{
    /// <summary>The profile screen: name, level and lifetime stats from the local profile.</summary>
    public class ProfilePanelUI : MonoBehaviour
    {
        public ProfileRules rules;

        [Header("Identity")]
        public TMP_InputField tmp_Name;
        public TextMeshProUGUI txt_Level;
        public TextMeshProUGUI txt_Experience;
        [Tooltip("Fill image of the experience bar (Image type Filled).")]
        public Image img_ExperienceFill;

        [Header("Stats")]
        public TextMeshProUGUI txt_Matches;
        public TextMeshProUGUI txt_WinsLosses;
        public TextMeshProUGUI txt_Kills;
        public TextMeshProUGUI txt_Deaths;
        public TextMeshProUGUI txt_KillDeath;
        public TextMeshProUGUI txt_Captures;
        public TextMeshProUGUI txt_TimePlayed;
        public TextMeshProUGUI txt_BestMatch;
        [Tooltip("One line per class played.")]
        public TextMeshProUGUI txt_Classes;

        public Button btn_Back;

        private void Awake()
        {
            if (tmp_Name != null)
            {
                tmp_Name.characterLimit = 24;
                tmp_Name.onEndEdit.AddListener(text => ProfileStore.SetName(text.Trim()));
            }
        }

        private void OnEnable()
        {
            Refresh();
            ProfileStore.Changed += Refresh;
        }

        private void OnDisable()
        {
            ProfileStore.Changed -= Refresh;
        }

        public void Refresh()
        {
            var p = ProfileStore.Current;
            if (tmp_Name != null && !tmp_Name.isFocused) tmp_Name.SetTextWithoutNotify(p.playerName);
            int level = 1, into = 0, needed = 1;
            if (rules != null) level = rules.LevelFor(p.experience, out into, out needed);
            Set(txt_Level, $"LEVEL {level}");
            Set(txt_Experience, $"{into} / {needed} XP");
            if (img_ExperienceFill != null) img_ExperienceFill.fillAmount = needed > 0 ? (float)into / needed : 0f;

            Set(txt_Matches, p.matchesPlayed.ToString());
            Set(txt_WinsLosses, $"{p.wins} / {p.losses}");
            Set(txt_Kills, p.kills.ToString());
            Set(txt_Deaths, p.deaths.ToString());
            Set(txt_KillDeath, p.deaths > 0 ? ((float)p.kills / p.deaths).ToString("0.00") : p.kills.ToString("0.00"));
            Set(txt_Captures, p.captures.ToString());
            Set(txt_TimePlayed, FormatTime(p.secondsPlayed));
            Set(txt_BestMatch, $"{p.bestMatchKills} kills");

            if (txt_Classes != null)
            {
                if (p.classes.Count == 0) txt_Classes.text = "Play a match to start your class record.";
                else
                {
                    var sb = new StringBuilder();
                    foreach (var c in p.classes)
                    {
                        if (sb.Length > 0) sb.Append('\n');
                        sb.Append($"<b>{c.className}</b><pos=34%>{c.matches} matches<pos=58%>{c.kills} kills<pos=78%>{c.deaths} deaths");
                    }
                    txt_Classes.text = sb.ToString();
                }
            }
        }

        /// <summary>Short label for the title screen's profile badge.</summary>
        public static string LevelLabel(ProfileRules rules)
        {
            var p = ProfileStore.Current;
            int level = rules != null ? rules.LevelFor(p.experience, out _, out _) : 1;
            return $"LEVEL {level}";
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.FloorToInt(seconds);
            int h = total / 3600, m = (total % 3600) / 60;
            return h > 0 ? $"{h}h {m}m" : $"{m}m {total % 60}s";
        }

        private static void Set(TextMeshProUGUI t, string s)
        {
            if (t != null) t.text = s;
        }

        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
    }
}
