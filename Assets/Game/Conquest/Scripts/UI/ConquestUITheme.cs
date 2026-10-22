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
        Divider = 11,
        Primary = 12,
        Secondary = 13
    }

    /// <summary>Which palette a button takes its state colours from.</summary>
    public enum UIButtonStyle
    {
        Primary = 0,
        Secondary = 1
    }

    /// <summary>A button's colour in each state. Applied as the Button's colour tint over a white graphic.</summary>
    [System.Serializable]
    public struct UIButtonColors
    {
        public Color normal;
        public Color hover;
        public Color pressed;
        public Color selected;
        public Color disabled;

        public UIButtonColors(Color normal, Color hover, Color pressed, Color selected, Color disabled)
        {
            this.normal = normal; this.hover = hover; this.pressed = pressed; this.selected = selected; this.disabled = disabled;
        }
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

        [Header("Palette")]
        [Tooltip("Main call-to-action colour: primary buttons, highlights, slider fills, selected items.")]
        public Color primary = new Color(0.2f, 0.6f, 1f);
        [Tooltip("Supporting colour: ordinary buttons, steppers and secondary controls.")]
        public Color secondary = new Color(0.25f, 0.30f, 0.40f);

        [Header("Buttons")]
        public UIButtonColors primaryButton = new UIButtonColors(
            new Color(0.2f, 0.6f, 1f), new Color(0.35f, 0.7f, 1f), new Color(0.12f, 0.42f, 0.78f),
            new Color(0.3f, 0.66f, 1f), new Color(0.2f, 0.28f, 0.38f, 0.7f));
        public UIButtonColors secondaryButton = new UIButtonColors(
            new Color(0.25f, 0.30f, 0.40f), new Color(0.34f, 0.41f, 0.53f), new Color(0.17f, 0.21f, 0.29f),
            new Color(0.3f, 0.36f, 0.47f), new Color(0.16f, 0.18f, 0.22f, 0.7f));
        public Color buttonText = Color.white;
        public Color buttonTextDisabled = new Color(0.55f, 0.58f, 0.62f);

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
        [Tooltip("Shown instead of the team icon on a team's home base. Empty uses the team icon.")]
        public Sprite homeBaseIcon;

        [Header("Weapon heat")]
        [Tooltip("Placeholder: heat gauge colour when cold.")]
        public Color heatCool = new Color(0.95f, 0.8f, 0.3f);
        [Tooltip("Placeholder: heat gauge colour near overheating.")]
        public Color heatHot = new Color(1f, 0.45f, 0.1f);
        [Tooltip("Placeholder: heat gauge colour while overheated.")]
        public Color overheated = new Color(0.95f, 0.2f, 0.15f);

        [Header("High contrast")]
        public Color panelHighContrast = new Color(0f, 0f, 0f, 1f);
        public Color textMutedHighContrast = new Color(0.9f, 0.92f, 0.95f);

        public Color TeamColor(Team team, bool colorBlind)
        {
            if (team == Team.Red) return colorBlind ? redTeamColorBlind : redTeam;
            if (team == Team.Blue) return colorBlind ? blueTeamColorBlind : blueTeam;
            return neutral;
        }

        public Color HeatColor(float heat01) => Color.Lerp(heatCool, heatHot, Mathf.Clamp01(heat01));

        public Sprite TeamIcon(Team team)
        {
            if (team == Team.Red) return redTeamIcon;
            if (team == Team.Blue) return blueTeamIcon;
            return neutralIcon;
        }

        /// <summary>The icon for a command post: the home base icon on a home base, otherwise its owner's team icon.</summary>
        public Sprite PostIcon(Game.Conquest.Objectives.ConquestCommandPost post)
        {
            if (post.IsHomeBase && homeBaseIcon != null) return homeBaseIcon;
            return TeamIcon(post.CurrentOwner);
        }

        public UIButtonColors Button(UIButtonStyle style) => style == UIButtonStyle.Primary ? primaryButton : secondaryButton;

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
                case UIColorRole.Button: return secondaryButton.normal;
                case UIColorRole.ButtonPrimary: return primaryButton.normal;
                case UIColorRole.ButtonDanger: return secondaryButton.normal;
                case UIColorRole.Primary: return primary;
                case UIColorRole.Secondary: return secondary;
                case UIColorRole.Track: return track;
                case UIColorRole.Fill: return fill;
                case UIColorRole.Divider: return divider;
                default: return text;
            }
        }
    }
}
