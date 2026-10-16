using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    public class SoloPanelUI : MonoBehaviour
    {
        [Header("Text Elements")]
        public TextMeshProUGUI txt_MapName;
        public TextMeshProUGUI txt_MapDescription;
        public TextMeshProUGUI txt_Difficulty;
        public TextMeshProUGUI txt_TeamSize;
        public TextMeshProUGUI txt_Tickets;

        [Header("Buttons")]
        public Button btn_NextMap;
        public Button btn_PrevMap;
        public Button btn_Start;
        public Button btn_Back;

        [Header("Toggles")]
        public Toggle tgl_Easy;
        public Toggle tgl_Normal;
        public Toggle tgl_Hard;

        [Header("Sliders")]
        public Slider sld_TargetTeamSize;
        public Slider sld_TeamTickets;


        // Show and hide the solo panel
        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);

    }
}
