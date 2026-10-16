using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    public class SettingsPanelUI : MonoBehaviour
    {
        [Header("Sliders")]
        public Slider masterVolume;
        public Slider musicVolume;
        public Slider sfxVolume;
        public Slider mouseSensitivity;
        public Slider fovSlider;

        [Header("Buttons")]
        public Button btn_Apply;
        public Button btn_Defaults;
        public Button btn_Back;

        // Show and hide the settings panel
        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
    }
}
