using TMPro;
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

        [Header("Value Labels")]
        public TextMeshProUGUI txt_MasterVolume;
        public TextMeshProUGUI txt_MusicVolume;
        public TextMeshProUGUI txt_SfxVolume;
        public TextMeshProUGUI txt_MouseSensitivity;
        public TextMeshProUGUI txt_Fov;

        [Header("Buttons")]
        public Button btn_Apply;
        public Button btn_Defaults;
        public Button btn_Back;
        public Button btn_Controls;

        [Header("Sub-panels")]
        public ControlsPanelUI controlsPanel;

        // Show and hide the settings panel
        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
    }
}
