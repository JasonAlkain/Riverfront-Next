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

        private InputAction m_Action;
        private int m_BindingIndex;

        public event Action<RebindRowUI> Clicked;

        public InputAction Action => m_Action;
        public int BindingIndex => m_BindingIndex;

        private void Awake()
        {
            if (btn_Rebind != null) btn_Rebind.onClick.AddListener(() => Clicked?.Invoke(this));
        }

        public void Setup(string label, InputAction action, int bindingIndex)
        {
            m_Action = action;
            m_BindingIndex = bindingIndex;
            if (txt_Label != null) txt_Label.text = label;
            Refresh();
        }

        public void Refresh()
        {
            if (txt_Binding == null || m_Action == null) return;
            txt_Binding.text = m_Action.GetBindingDisplayString(m_BindingIndex);
        }

        public void ShowListening()
        {
            if (txt_Binding != null) txt_Binding.text = "Press a key... (Esc cancels)";
        }
    }
}
