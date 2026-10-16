using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    public class CaptureBarUI : MonoBehaviour
    {
        [Header("Images")]
        [Tooltip("Image Type must be Filled (Horizontal) so fillAmount shows progress.")]
        public Image img_Fill;

        [Header("Text Elements")]
        public TextMeshProUGUI txt_Status;

        public void SetProgress(float progress, Color color, string status)
        {
            if (img_Fill != null)
            {
                img_Fill.fillAmount = Mathf.Clamp01(progress);
                img_Fill.color = color;
            }
            if (txt_Status != null) txt_Status.text = status;
        }

        // Show and hide the capture bar
        public void Show() { if (!gameObject.activeSelf) gameObject.SetActive(true); }
        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }
    }
}
