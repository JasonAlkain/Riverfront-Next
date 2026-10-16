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

        // Show and hide the header banner
        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
    }
}
