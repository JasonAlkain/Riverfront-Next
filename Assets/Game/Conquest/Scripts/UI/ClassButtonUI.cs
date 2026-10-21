using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Game.Conquest.Classes;

namespace Game.Conquest
{
    /// <summary>One class choice: name, health and weapon from its ConquestClassData, with an accent stripe.</summary>
    public class ClassButtonUI : MonoBehaviour
    {
        [SerializeField] private Button btn_Class;
        [SerializeField] private Image img_Background;
        [SerializeField] private Image img_Accent;
        [SerializeField] private TextMeshProUGUI txt_Name;
        [SerializeField] private TextMeshProUGUI txt_Details;

        private int m_Index;
        private int m_Selected = -1; // -1 unknown, 0 no, 1 yes

        /// <summary>Raised with this button's class index when clicked.</summary>
        public event Action<int> Clicked;

        private void Awake()
        {
            if (btn_Class != null) btn_Class.onClick.AddListener(() => Clicked?.Invoke(m_Index));
        }

        public void SetClass(int index, ConquestClassData data)
        {
            m_Index = index;
            if (data == null) return;
            if (txt_Name != null) txt_Name.text = data.className;
            if (txt_Details != null) txt_Details.text = $"{data.maxHealth} HP\n{data.weaponName}";
            if (img_Accent != null) img_Accent.color = data.classAccentColor;
        }

        public void SetSelected(bool selected, Color normalColor, Color selectedColor)
        {
            int state = selected ? 1 : 0;
            if (state == m_Selected) return;
            m_Selected = state;
            if (img_Background != null) img_Background.color = selected ? selectedColor : normalColor;
        }
    }
}
