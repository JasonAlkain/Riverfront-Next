using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>A settings row with "&lt; value &gt;" buttons that step through a list of options (wrapping).</summary>
    public class OptionStepperUI : MonoBehaviour
    {
        [Header("Elements")]
        public TextMeshProUGUI txt_Label;
        public Button btn_Prev;
        public Button btn_Next;
        public TextMeshProUGUI txt_Value;

        private readonly List<string> m_Options = new List<string>();
        private int m_Index;

        /// <summary>Raised with the new index when the player steps.</summary>
        public event Action<int> Changed;

        public int Index => m_Index;

        private void Awake()
        {
            if (btn_Prev != null) btn_Prev.onClick.AddListener(() => Step(-1));
            if (btn_Next != null) btn_Next.onClick.AddListener(() => Step(1));
        }

        public void SetOptions(IReadOnlyList<string> options, int index)
        {
            m_Options.Clear();
            if (options != null) m_Options.AddRange(options);
            SetIndexWithoutNotify(index);
        }

        public void SetIndexWithoutNotify(int index)
        {
            m_Index = m_Options.Count > 0 ? Mathf.Clamp(index, 0, m_Options.Count - 1) : 0;
            if (txt_Value != null) txt_Value.text = m_Options.Count > 0 ? m_Options[m_Index] : "";
        }

        private void Step(int delta)
        {
            if (m_Options.Count == 0) return;
            SetIndexWithoutNotify((m_Index + delta + m_Options.Count) % m_Options.Count);
            Changed?.Invoke(m_Index);
        }
    }
}
