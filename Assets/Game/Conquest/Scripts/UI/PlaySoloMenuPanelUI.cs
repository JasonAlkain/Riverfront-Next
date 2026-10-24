using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>
    /// The Play Solo submenu: Tutorial and Campaign are shown but not available yet; Practice
    /// opens the solo Conquest setup (SoloPanel).
    /// </summary>
    public class PlaySoloMenuPanelUI : MonoBehaviour
    {
        [Header("Buttons")]
        [Tooltip("Coming soon: left non-interactable.")]
        public Button btn_Tutorial;
        public Button btn_Practice;
        [Tooltip("Coming soon: left non-interactable.")]
        public Button btn_Campaign;
        public Button btn_Back;

        // Show and hide the Play Solo submenu
        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
    }
}
