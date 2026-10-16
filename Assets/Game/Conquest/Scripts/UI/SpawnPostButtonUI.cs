using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    public class SpawnPostButtonUI : MonoBehaviour
    {
        [SerializeField] private Button btn_Spawn;
        [SerializeField] private Image img_Background;
        [SerializeField] private TextMeshProUGUI txt_PostName;

        private string m_PostName;
        private bool m_IsSelected;

        /// <summary>Raised with this button's post name when it is clicked.</summary>
        public event Action<string> Clicked;

        private void Awake()
        {
            if (btn_Spawn != null) btn_Spawn.onClick.AddListener(() => Clicked?.Invoke(m_PostName));
        }

        public void SetPost(string postName, bool isSelected, Color normalColor, Color selectedColor)
        {
            if (postName == m_PostName && isSelected == m_IsSelected) return;
            m_PostName = postName;
            m_IsSelected = isSelected;

            if (img_Background != null) img_Background.color = isSelected ? selectedColor : normalColor;
            if (txt_PostName != null) txt_PostName.text = isSelected ? $"> Post {postName} <" : $"Post {postName}";
        }
    }
}
