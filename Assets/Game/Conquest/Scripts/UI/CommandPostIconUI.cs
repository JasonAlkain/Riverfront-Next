using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    public class CommandPostIconUI : MonoBehaviour
    {
        [SerializeField] private Image img_Background;
        [SerializeField] private TextMeshProUGUI txt_Label;

        private string m_Label;

        public void SetPost(string label, Color color)
        {
            if (img_Background != null) img_Background.color = color;
            if (txt_Label != null && label != m_Label)
            {
                txt_Label.text = label;
                m_Label = label;
            }
        }
    }
}
