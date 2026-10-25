using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    public class TitlePanelUI : MonoBehaviour
    {
        [Header("Profile badge")]
        [Tooltip("Top-right badge with the profile name and level; opens the profile screen.")]
        public Button btn_Profile;
        public TMPro.TextMeshProUGUI txt_ProfileName;
        public TMPro.TextMeshProUGUI txt_ProfileLevel;

        [Header("Buttons")]
        public Button btn_Singleplayer;
        public Button btn_Multiplayer;
        public Button btn_Settings;
        public Button btn_Credits;
        public Button btn_Quit;


        // Show and hide the title panel
        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
    }
}
