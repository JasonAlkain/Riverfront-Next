using TMPro;
using UnityEngine;

namespace Game.Conquest
{
    public class CrosshairUI : MonoBehaviour
    {
        [Header("Text Elements")]
        public TextMeshProUGUI txt_Crosshair;

        [Header("Look")]
        public string normalGlyph = "+";
        public string hitGlyph = "X";
        public Color normalColor = new Color(1f, 1f, 1f, 0.8f);
        public Color hitColor = Color.red;

        public void SetHitmarker(bool showHit)
        {
            if (txt_Crosshair == null) return;
            txt_Crosshair.text = showHit ? hitGlyph : normalGlyph;
            txt_Crosshair.color = showHit ? hitColor : normalColor;
        }

        // Show and hide the crosshair
        public void Show() { if (!gameObject.activeSelf) gameObject.SetActive(true); }
        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }
    }
}
