using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Controls;
using Game.Conquest.Player;
using Game.Conquest.Networking;
using Game.Conquest.Gameplay;

namespace Game.Conquest.UI
{
    /// <summary>
    /// Pause logic for a match. The visuals are the PauseMenuCanvas prefab (PauseMenuUI), created
    /// as a child at startup; Settings inside it is the same SettingsScreen the main menu uses.
    /// Pausing never stops the simulation: it only frees the cursor and blocks local input.
    /// </summary>
    public class ConquestPauseMenu : MonoBehaviour
    {
        public static ConquestPauseMenu Instance { get; private set; }

        [SerializeField] private PauseMenuUI pauseMenuPrefab;

        private PauseMenuUI m_UI;
        private bool m_IsPaused;

        public bool IsPaused => m_IsPaused;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // A dedicated server has no one to pause for; IsPaused stays false.
            if (ConquestServerLaunchArgs.IsDedicatedServerLaunch() || pauseMenuPrefab == null)
            {
                if (pauseMenuPrefab == null) Debug.LogWarning("[ConquestPauseMenu] No pause menu prefab assigned.", this);
                enabled = false;
                return;
            }

            m_UI = Instantiate(pauseMenuPrefab, transform);
            Bind();
            m_UI.canvas.enabled = false;
            if (m_UI.matchEndPanel != null) m_UI.matchEndPanel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Bind()
        {
            On(m_UI.btn_Resume, () => SetPause(false));
            On(m_UI.btn_Settings, () => m_UI.ShowScreen(PauseMenuUI.PauseScreen.Settings));
            On(m_UI.btn_Leave, () => m_UI.ShowScreen(PauseMenuUI.PauseScreen.LeaveConfirm));
            On(m_UI.btn_Quit, QuitApplication);
            On(m_UI.btn_CancelLeave, () => m_UI.ShowScreen(PauseMenuUI.PauseScreen.Main));
            On(m_UI.btn_ConfirmLeave, () =>
            {
                SetPause(false);
                ConquestSession.Current?.LeaveSession();
            });
            // Everyone goes to the results screen and back to the lobby automatically; this only
            // lets a client drop out early. Hidden on the host, where leaving would end the session for all.
            On(m_UI.btn_LeaveGame, () => ConquestSession.Current?.LeaveSession());
            if (m_UI.settingsPanel != null) m_UI.settingsPanel.Closed += () => m_UI.ShowScreen(PauseMenuUI.PauseScreen.Main);
        }

        private static void On(UnityEngine.UI.Button button, UnityEngine.Events.UnityAction onClick)
        {
            if (button != null) button.onClick.AddListener(onClick);
        }

        private void Update()
        {
            // Match over: results and the lobby follow automatically
            var mm = ConquestMatchManager.Instance;
            if (mm != null && mm.IsMatchOver)
            {
                bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
                m_UI.canvas.enabled = true;
                if (m_UI.matchEndPanel != null && m_UI.matchEndPanel.activeSelf == isServer) m_UI.matchEndPanel.SetActive(!isServer);
                m_UI.ShowScreen(PauseMenuUI.PauseScreen.None);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            // Pause action: Escape, or Start on a gamepad. Inside a sub-screen it steps back first.
            if (ConquestInput.PausePressed)
            {
                if (m_IsPaused && m_UI.IsInSubScreen) m_UI.ShowScreen(PauseMenuUI.PauseScreen.Main);
                else TogglePause();
            }
        }

        public void TogglePause()
        {
            SetPause(!m_IsPaused);
        }

        public void SetPause(bool paused)
        {
            if (m_UI == null) return;
            m_IsPaused = paused;
            m_UI.canvas.enabled = paused;
            m_UI.ShowScreen(paused ? PauseMenuUI.PauseScreen.Main : PauseMenuUI.PauseScreen.None);
            if (m_UI.matchEndPanel != null) m_UI.matchEndPanel.SetActive(false);

            var player = ConquestPlayer.Local;
            if (paused)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (player != null && player.Movement != null) player.Movement.SetCursorLock(false);
            }
            else if (player != null && !player.Health.IsDead)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                if (player.Movement != null) player.Movement.SetCursorLock(true);
            }
        }

        private void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
