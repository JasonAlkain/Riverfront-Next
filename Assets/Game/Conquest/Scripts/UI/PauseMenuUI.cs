using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>The PauseMenuCanvas prefab: pause buttons, leave confirmation, the shared Settings screen and the match-end button.</summary>
    public class PauseMenuUI : MonoBehaviour
    {
        [Header("Canvas")]
        public Canvas canvas;

        [Header("Main")]
        public GameObject mainPanel;
        public Button btn_Resume;
        public Button btn_Settings;
        public Button btn_Leave;
        public Button btn_Quit;

        [Header("Leave confirmation")]
        public GameObject leaveConfirmPanel;
        public Button btn_ConfirmLeave;
        public Button btn_CancelLeave;

        [Header("Quit confirmation")]
        [Tooltip("Quitting ends the match for this player (and stops play mode in the Editor), so it asks first.")]
        public GameObject quitConfirmPanel;
        public Button btn_ConfirmQuit;
        public Button btn_CancelQuit;

        [Header("Settings")]
        public SettingsPanelUI settingsPanel;
        public ControlsPanelUI controlsPanel;

        [Header("Match over")]
        [Tooltip("Lets a client drop out early once the match has ended.")]
        public GameObject matchEndPanel;
        public Button btn_LeaveGame;

        public enum PauseScreen { None, Main, Settings, LeaveConfirm, QuitConfirm }

        public void ShowScreen(PauseScreen screen)
        {
            if (mainPanel != null) mainPanel.SetActive(screen == PauseScreen.Main);
            if (leaveConfirmPanel != null) leaveConfirmPanel.SetActive(screen == PauseScreen.LeaveConfirm);
            if (quitConfirmPanel != null) quitConfirmPanel.SetActive(screen == PauseScreen.QuitConfirm);
            if (settingsPanel != null) settingsPanel.gameObject.SetActive(screen == PauseScreen.Settings);
            if (controlsPanel != null && screen != PauseScreen.Settings) controlsPanel.gameObject.SetActive(false);
        }

        /// <summary>True while a sub-screen (settings, controls, confirm) is open over the main pause panel.</summary>
        public bool IsInSubScreen =>
            (settingsPanel != null && settingsPanel.gameObject.activeSelf) ||
            (controlsPanel != null && controlsPanel.gameObject.activeSelf) ||
            (leaveConfirmPanel != null && leaveConfirmPanel.activeSelf) ||
            (quitConfirmPanel != null && quitConfirmPanel.activeSelf);
    }
}
