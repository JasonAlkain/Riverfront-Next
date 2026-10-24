using TMPro;
using UnityEngine;

namespace Game.Conquest
{
    public class HeaderBannerUI : MonoBehaviour
    {
        [Header("Text Elements")]
        public TextMeshProUGUI txt_Title;
        public TextMeshProUGUI txt_SubTitle;
        public TextMeshProUGUI txt_StatusBadge;

        /// <summary>The title screen draws its own large logo, so the banner's title and subtitle step aside there.</summary>
        public void SetTitleVisible(bool visible)
        {
            SetActive(txt_Title, visible);
            SetActive(txt_SubTitle, visible);
        }

        // The texts sit inside holder objects that are laid out on the banner; hide the holder.
        private static void SetActive(TextMeshProUGUI text, bool visible)
        {
            if (text == null) return;
            var target = text.transform.parent != transform && text.transform.parent != null ? text.transform.parent.gameObject : text.gameObject;
            if (target.activeSelf != visible) target.SetActive(visible);
        }

        // Show and hide the header banner
        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
    }
}
