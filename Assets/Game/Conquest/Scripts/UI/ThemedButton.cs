using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Game.Conquest.UI;

namespace Game.Conquest
{
    /// <summary>
    /// Gives a Button the theme's primary or secondary state colours (normal, hover, pressed,
    /// selected, disabled) as its colour tint over a white graphic, and its label the button text colour.
    /// Buttons whose colour code sets for a selection (class and spawn buttons) don't use this.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ThemedButton : MonoBehaviour
    {
        public ConquestUITheme theme;
        public UIButtonStyle style = UIButtonStyle.Secondary;

        private Button m_Button;
        private TMP_Text m_Label;
        private bool m_WasInteractable;

        private void Awake()
        {
            m_Button = GetComponent<Button>();
            m_Button.onClick.AddListener(PlayClick);
        }

        private void OnEnable()
        {
            Apply();
        }

        private void PlayClick()
        {
            if (theme != null) Game.Conquest.Audio.ConquestUIAudio.Play(theme.clickSound, theme.clickVolume, theme.uiMixerGroup);
        }

        private void Update()
        {
            // Label dims with the button; the graphic is handled by the colour tint.
            if (m_Button != null && m_Button.interactable != m_WasInteractable) ApplyLabel();
        }

        public void Apply()
        {
            if (theme == null) return;
            if (m_Button == null) m_Button = GetComponent<Button>();
            if (m_Label == null) m_Label = GetComponentInChildren<TMP_Text>(true);

            var c = theme.Button(style);
            m_Button.transition = Selectable.Transition.ColorTint;
            var block = m_Button.colors;
            block.normalColor = c.normal;
            block.highlightedColor = c.hover;
            block.pressedColor = c.pressed;
            block.selectedColor = c.selected;
            block.disabledColor = c.disabled;
            block.colorMultiplier = 1f;
            block.fadeDuration = 0.08f;
            m_Button.colors = block;
            if (m_Button.targetGraphic != null) m_Button.targetGraphic.color = Color.white;
            ApplyLabel();
        }

        private void ApplyLabel()
        {
            m_WasInteractable = m_Button.interactable;
            if (m_Label == null) return;
            m_Label.color = m_WasInteractable ? theme.buttonText : theme.buttonTextDisabled;
            if (theme.font != null && m_Label.font != theme.font) m_Label.font = theme.font;
        }
    }
}
