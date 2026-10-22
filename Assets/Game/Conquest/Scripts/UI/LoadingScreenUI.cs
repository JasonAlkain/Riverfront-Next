using TMPro;
using UnityEngine;

namespace Game.Conquest
{
    /// <summary>The LoadingScreenCanvas prefab: destination name and a gameplay tip over a full-screen backdrop.</summary>
    public class LoadingScreenUI : MonoBehaviour
    {
        public CanvasGroup canvasGroup;
        public TextMeshProUGUI txt_Title;
        public TextMeshProUGUI txt_MapName;
        public TextMeshProUGUI txt_Tip;

        public void SetContent(string mapName, string tip)
        {
            if (txt_MapName != null) txt_MapName.text = mapName.ToUpperInvariant();
            if (txt_Tip != null) txt_Tip.text = tip;
        }

        public float Alpha
        {
            get => canvasGroup != null ? canvasGroup.alpha : 0f;
            set
            {
                if (canvasGroup == null) return;
                canvasGroup.alpha = value;
                canvasGroup.blocksRaycasts = value > 0f;
            }
        }
    }
}
