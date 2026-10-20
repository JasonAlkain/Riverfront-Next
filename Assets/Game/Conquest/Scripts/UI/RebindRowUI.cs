using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>One row of the Controls panel: what the binding does and the key it is on. Click to rebind.</summary>
    public class RebindRowUI : MonoBehaviour
    {
        [Header("Elements")]
        public TextMeshProUGUI txt_Label;
        public TextMeshProUGUI txt_Binding;
        public Button btn_Rebind;
        public Image img_KeyBackground;

        [Header("Look")]
        public Color keyColor = new Color(0.22f, 0.28f, 0.36f, 1f);
        public Color listeningColor = new Color(0.35f, 0.78f, 1f, 1f);
        public Color keyTextColor = Color.white;
        public Color conflictTextColor = new Color(1f, 0.55f, 0.2f, 1f);

        private InputAction m_Action;
        private int m_BindingIndex;

        public event Action<RebindRowUI> Clicked;

        public InputAction Action => m_Action;
        public int BindingIndex => m_BindingIndex;
        /// <summary>The control path this row is bound to now, including any rebind.</summary>
        public string EffectivePath => m_Action != null ? m_Action.bindings[m_BindingIndex].effectivePath : "";

        private void Awake()
        {
            if (btn_Rebind != null) btn_Rebind.onClick.AddListener(() => Clicked?.Invoke(this));
        }

        public void Setup(string label, InputAction action, int bindingIndex)
        {
            m_Action = action;
            m_BindingIndex = bindingIndex;
            if (txt_Label != null) txt_Label.text = label;
            Refresh(false);
        }

        /// <param name="conflict">Another row uses the same key: shown in a warning colour.</param>
        public void Refresh(bool conflict)
        {
            if (img_KeyBackground != null) img_KeyBackground.color = keyColor;
            if (txt_Binding == null || m_Action == null) return;
            txt_Binding.text = m_Action.GetBindingDisplayString(m_BindingIndex);
            txt_Binding.color = conflict ? conflictTextColor : keyTextColor;
        }

        public void ShowListening()
        {
            if (img_KeyBackground != null) img_KeyBackground.color = listeningColor;
            if (txt_Binding != null)
            {
                txt_Binding.text = "Press a key";
                txt_Binding.color = keyTextColor;
            }
        }
    }
}
