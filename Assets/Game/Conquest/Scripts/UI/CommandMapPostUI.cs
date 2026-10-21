using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>One command post on the command map. Clickable on the deploy screen to pick a spawn.</summary>
    public class CommandMapPostUI : MonoBehaviour
    {
        [SerializeField] private Button btn_Post;
        [SerializeField] private Image img_Icon;
        [Tooltip("Ring shown around the post chosen for the next deploy.")]
        [SerializeField] private GameObject selectedRing;
        [SerializeField] private TextMeshProUGUI txt_Label;

        private string m_PostName;
        private Color m_Color;
        private bool m_Selected;
        private bool m_Selectable = true;

        /// <summary>Raised with the post name when clicked.</summary>
        public event Action<string> Clicked;

        public RectTransform Rect => (RectTransform)transform;

        private void Awake()
        {
            if (btn_Post != null) btn_Post.onClick.AddListener(() => Clicked?.Invoke(m_PostName));
            if (selectedRing != null) selectedRing.SetActive(false);
        }

        public void SetPost(string postName, Color color, bool selectable, bool selected)
        {
            if (postName != m_PostName)
            {
                m_PostName = postName;
                if (txt_Label != null) txt_Label.text = postName;
            }
            if (color != m_Color)
            {
                m_Color = color;
                if (img_Icon != null) img_Icon.color = color;
            }
            if (selectable != m_Selectable)
            {
                m_Selectable = selectable;
                if (btn_Post != null) btn_Post.interactable = selectable;
            }
            if (selected != m_Selected)
            {
                m_Selected = selected;
                if (selectedRing != null) selectedRing.SetActive(selected);
            }
        }
    }
}
