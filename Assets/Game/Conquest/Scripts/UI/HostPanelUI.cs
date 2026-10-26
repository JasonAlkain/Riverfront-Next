using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Game.Conquest
{
    public class HostPanelUI : MonoBehaviour
    {
        [Header("Input Fields")]
        public TMP_InputField tmp_SessionName;

        [Header("Text Elements")]
        public TextMeshProUGUI txt_SelectedMap;
        public TextMeshProUGUI txt_MaxHumanConnections;
        public TextMeshProUGUI txt_ReinforcementTickets;
        public TextMeshProUGUI txt_TargetTeamSize;
        [Tooltip("Optional: the selected map's description.")]
        public TextMeshProUGUI txt_MapDescription;

        [Header("Map preview")]
        [Tooltip("Optional: the map's preview image, or its colour when it has none.")]
        public Image img_MapPreview;

        [Header("Slider")]
        public Slider sld_MaxHumanConnections;
        public Slider sld_ReinforcementTickets;
        public Slider sld_TargetTeamSize;

        [Header("Network")]
        [Tooltip("Online (Relay, players join with a code) or LAN (players type this PC's IP).")]
        public OptionStepperUI stp_Network;

        [Header("Bots")]
        [Tooltip("Off: only humans play. On: bots fill every team up to the target size.")]
        public OptionStepperUI stp_BotFill;
        public OptionStepperUI stp_BotDifficulty;

        [Header("Buttons")]
        public Button btn_PrevMap;
        public Button btn_NextMap;
        public Button btn_CreateLobby;
        public Button btn_Back;

        // Show and hide the host panel UI
        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
    }
}
