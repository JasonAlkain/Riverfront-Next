using TMPro;
using UnityEngine;
using Game.Conquest.Gameplay;

namespace Game.Conquest.UI
{
    /// <summary>Colour roles a themed graphic can take. Values are stable: they are serialized.</summary>
    public enum UIColorRole
    {
        Backdrop = 0,
        Panel = 1,
        PanelAlt = 2,
        Accent = 3,
        Text = 4,
        TextMuted = 5,
        Button = 6,
        ButtonPrimary = 7,
        ButtonDanger = 8,
        Track = 9,
        Fill = 10,
        Divider = 11
    }

    /// <summary>
    /// One place for the UI's colours and font. Themed graphics read their colour from here by
    /// role, and the high-contrast option swaps panels and muted text for stronger values.
    /// </summary>
    [CreateAssetMenu(fileName = "UITheme", menuName = "Conquest/UI Theme")]
    public class ConquestUITheme : ScriptableObject
    {
        [Header("Font")]
        public TMP_FontAsset font;

        [Header("Surfaces")]
        public Color backdrop = new Color(0.04f, 0.06f, 0.09f, 0.85f);
        public Color panel = new Color(0.08f, 0.11f, 0.16f, 0.96f);
        public Color panelAlt = new Color(0.12f, 0.16f, 0.24f, 0.9f);
        public Color divider = new Color(0.35f, 0.78f, 1.0f, 0.25f);

        [Header("Text")]
        public Color accent = new Color(0.35f, 0.78f, 1.0f);
        public Color text = Color.white;
        public Color textMuted = new Color(0.55f, 0.62f, 0.72f);

        [Header("Controls")]
        public Color button = new Color(0.25f, 0.30f, 0.40f);
        public Color buttonPrimary = new Color(0.2f, 0.6f, 1f);
        public Color buttonDanger = new Color(0.85f, 0.3f, 0.2f);
        public Color track = new Color(0.2f, 0.25f, 0.32f);
        public Color fill = new Color(0.3f, 0.75f, 1.0f);

        [Header("Teams")]
        public Color redTeam = new Color(0.85f, 0.2f, 0.2f, 0.9f);
        public Color blueTeam = new Color(0.2f, 0.5f, 0.9f, 0.9f);
        public Color neutral = new Color(0.4f, 0.4f, 0.45f, 0.9f);
        public Color contested = new Color(0.9f, 0.75f, 0.1f, 0.95f);

        [Header("Colour-blind team palette")]
        [Tooltip("Used instead of red/blue when the colour-blind option is on (orange vs blue reads for most colour vision types).")]
        public Color redTeamColorBlind = new Color(0.95f, 0.55f, 0.1f, 0.9f);
        public Color blueTeamColorBlind = new Color(0.25f, 0.55f, 1f, 0.9f);

        [Header("Team icons (shape cue, so team never depends on colour alone)")]
        public Sprite redTeamIcon;
        public Sprite blueTeamIcon;
        public Sprite neutralIcon;

        [Header("High contrast")]
        public Color panelHighContrast = new Color(0f, 0f, 0f, 1f);
        public Color textMutedHighContrast = new Color(0.9f, 0.92f, 0.95f);

        public Color TeamColor(Team team, bool colorBlind)
        {
            if (team == Team.Red) return colorBlind ? redTeamColorBlind : redTeam;
            if (team == Team.Blue) return colorBlind ? blueTeamColorBlind : blueTeam;
            return neutral;
        }

        public Sprite TeamIcon(Team team)
        {
            if (team == Team.Red) return redTeamIcon;
            if (team == Team.Blue) return blueTeamIcon;
            return neutralIcon;
        }

        public Color Get(UIColorRole role, bool highContrast)
        {
            switch (role)
            {
                case UIColorRole.Backdrop: return backdrop;
                case UIColorRole.Panel: return highContrast ? panelHighContrast : panel;
                case UIColorRole.PanelAlt: return highContrast ? panelHighContrast : panelAlt;
                case UIColorRole.Accent: return accent;
                case UIColorRole.Text: return text;
                case UIColorRole.TextMuted: return highContrast ? textMutedHighContrast : textMuted;
                case UIColorRole.Button: return button;
                case UIColorRole.ButtonPrimary: return buttonPrimary;
                case UIColorRole.ButtonDanger: return buttonDanger;
                case UIColorRole.Track: return track;
                case UIColorRole.Fill: return fill;
                case UIColorRole.Divider: return divider;
                default: return text;
            }
        }
    }
}
