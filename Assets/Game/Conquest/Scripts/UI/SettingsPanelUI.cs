using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Game.Conquest.Settings;

namespace Game.Conquest
{
    /// <summary>
    /// The one Settings screen, used by the main menu and the pause menu (SettingsScreen prefab).
    /// It edits a copy of the current settings and applies it with Apply. A resolution or window
    /// mode change asks to be kept and reverts on its own if nobody answers.
    /// </summary>
    public class SettingsPanelUI : MonoBehaviour
    {
        [Header("Display")]
        public OptionStepperUI displayMode;
        public OptionStepperUI resolution;
        public OptionStepperUI quality;
        public OptionStepperUI vSync;
        public OptionStepperUI frameCap;
        public SettingSliderUI fieldOfView;

        [Header("Audio")]
        public SettingSliderUI masterVolume;
        public SettingSliderUI musicVolume;
        public SettingSliderUI sfxVolume;

        [Header("Controls")]
        public SettingSliderUI mouseSensitivity;
        public SettingSliderUI zoomSensitivity;
        public OptionStepperUI invertY;

        [Header("Accessibility")]
        public SettingSliderUI uiScale;
        public OptionStepperUI highContrast;
        public OptionStepperUI reduceMotion;
        public OptionStepperUI colorBlindTeams;

        [Header("Buttons")]
        public Button btn_Apply;
        public Button btn_Defaults;
        public Button btn_Back;
        public Button btn_Controls;

        [Header("Sub-panels")]
        public ControlsPanelUI controlsPanel;

        [Header("Keep display changes?")]
        public GameObject confirmDisplayPanel;
        public TextMeshProUGUI txt_ConfirmCountdown;
        public Button btn_KeepDisplay;
        public Button btn_RevertDisplay;
        [Tooltip("Seconds before an unconfirmed resolution or window mode change reverts.")]
        public float revertSeconds = 15f;

        /// <summary>Raised when Back is pressed; the owner decides which screen to show next.</summary>
        public event Action Closed;

        private static readonly string[] k_DisplayModes = { "Fullscreen", "Borderless", "Windowed" };
        private static readonly string[] k_OnOff = { "Off", "On" };
        private static readonly int[] k_FrameCaps = { 30, 60, 120, 144, 0 };
        private static readonly string[] k_FrameCapNames = { "30", "60", "120", "144", "Unlimited" };

        private readonly List<Vector2Int> m_Resolutions = new List<Vector2Int>();
        private readonly List<string> m_ResolutionNames = new List<string>();
        private ConquestSettingsData m_PreviousDisplay;
        private float m_RevertAt = -1f;
        private int m_ShownSeconds = -1;

        private void Awake()
        {
            if (btn_Apply != null) btn_Apply.onClick.AddListener(Apply);
            if (btn_Defaults != null) btn_Defaults.onClick.AddListener(RestoreDefaults);
            if (btn_Back != null) btn_Back.onClick.AddListener(Close);
            if (btn_Controls != null) btn_Controls.onClick.AddListener(OpenControls);
            if (btn_KeepDisplay != null) btn_KeepDisplay.onClick.AddListener(KeepDisplay);
            if (btn_RevertDisplay != null) btn_RevertDisplay.onClick.AddListener(RevertDisplay);
            if (controlsPanel != null) controlsPanel.returnPanel = gameObject;
        }

        private void OnEnable()
        {
            LoadFromSettings();
        }

        private void OnDisable()
        {
            // Leaving with a pending confirmation keeps the change (the player saw it and moved on).
            if (m_RevertAt >= 0f) KeepDisplay();
        }

        private void Update()
        {
            if (m_RevertAt < 0f) return;
            float left = m_RevertAt - Time.unscaledTime;
            if (left <= 0f)
            {
                RevertDisplay();
                return;
            }
            int seconds = Mathf.CeilToInt(left);
            if (seconds != m_ShownSeconds && txt_ConfirmCountdown != null)
            {
                m_ShownSeconds = seconds;
                txt_ConfirmCountdown.text = $"Keep these display settings?\nReverting in {seconds} s";
            }
        }

        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);

        private void Close()
        {
            Closed?.Invoke();
        }

        private void OpenControls()
        {
            if (controlsPanel != null) controlsPanel.Open();
        }

        public void LoadFromSettings()
        {
            var sm = ConquestSettingsManager.Instance;
            if (sm == null) return;
            var s = sm.CurrentSettings;

            displayMode?.SetOptions(k_DisplayModes, s.displayMode);
            BuildResolutions(s.resolutionWidth, s.resolutionHeight);
            resolution?.SetOptions(m_ResolutionNames, IndexOfResolution(s.resolutionWidth, s.resolutionHeight));
            quality?.SetOptions(QualitySettings.names, s.qualityLevel);
            vSync?.SetOptions(k_OnOff, s.vSync ? 1 : 0);
            frameCap?.SetOptions(k_FrameCapNames, Mathf.Max(0, Array.IndexOf(k_FrameCaps, s.targetFrameRate)));
            fieldOfView?.SetValueWithoutNotify(s.fieldOfView);

            masterVolume?.SetValueWithoutNotify(s.masterVolume);
            musicVolume?.SetValueWithoutNotify(s.musicVolume);
            sfxVolume?.SetValueWithoutNotify(s.sfxVolume);

            mouseSensitivity?.SetValueWithoutNotify(s.mouseSensitivity);
            zoomSensitivity?.SetValueWithoutNotify(s.zoomSensitivity);
            invertY?.SetOptions(k_OnOff, s.invertY ? 1 : 0);

            uiScale?.SetValueWithoutNotify(s.uiScale);
            highContrast?.SetOptions(k_OnOff, s.highContrast ? 1 : 0);
            reduceMotion?.SetOptions(k_OnOff, s.reduceMotion ? 1 : 0);
            colorBlindTeams?.SetOptions(k_OnOff, s.colorBlindTeams ? 1 : 0);
        }

        private void Apply()
        {
            var sm = ConquestSettingsManager.Instance;
            if (sm == null) return;
            var before = sm.CurrentSettings.Clone();
            var s = before.Clone();

            if (displayMode != null) s.displayMode = displayMode.Index;
            if (resolution != null && resolution.Index < m_Resolutions.Count)
            {
                s.resolutionWidth = m_Resolutions[resolution.Index].x;
                s.resolutionHeight = m_Resolutions[resolution.Index].y;
            }
            if (quality != null) s.qualityLevel = quality.Index;
            if (vSync != null) s.vSync = vSync.Index == 1;
            if (frameCap != null) s.targetFrameRate = k_FrameCaps[Mathf.Clamp(frameCap.Index, 0, k_FrameCaps.Length - 1)];
            if (fieldOfView != null) s.fieldOfView = fieldOfView.Value;

            if (masterVolume != null) s.masterVolume = masterVolume.Value;
            if (musicVolume != null) s.musicVolume = musicVolume.Value;
            if (sfxVolume != null) s.sfxVolume = sfxVolume.Value;

            if (mouseSensitivity != null) s.mouseSensitivity = mouseSensitivity.Value;
            if (zoomSensitivity != null) s.zoomSensitivity = zoomSensitivity.Value;
            if (invertY != null) s.invertY = invertY.Index == 1;

            if (uiScale != null) s.uiScale = uiScale.Value;
            if (highContrast != null) s.highContrast = highContrast.Index == 1;
            if (reduceMotion != null) s.reduceMotion = reduceMotion.Index == 1;
            if (colorBlindTeams != null) s.colorBlindTeams = colorBlindTeams.Index == 1;

            sm.ApplySettings(s);

            if (s.DisplayDiffers(before) && confirmDisplayPanel != null)
            {
                m_PreviousDisplay = before;
                m_RevertAt = Time.unscaledTime + revertSeconds;
                m_ShownSeconds = -1;
                confirmDisplayPanel.SetActive(true);
            }
        }

        private void KeepDisplay()
        {
            m_RevertAt = -1f;
            m_PreviousDisplay = null;
            if (confirmDisplayPanel != null) confirmDisplayPanel.SetActive(false);
        }

        private void RevertDisplay()
        {
            var sm = ConquestSettingsManager.Instance;
            if (sm != null && m_PreviousDisplay != null)
            {
                var s = sm.CurrentSettings.Clone();
                s.displayMode = m_PreviousDisplay.displayMode;
                s.resolutionWidth = m_PreviousDisplay.resolutionWidth;
                s.resolutionHeight = m_PreviousDisplay.resolutionHeight;
                sm.ApplySettings(s);
            }
            KeepDisplay();
            LoadFromSettings();
        }

        private void RestoreDefaults()
        {
            var sm = ConquestSettingsManager.Instance;
            if (sm == null) return;
            sm.RestoreDefaults();
            LoadFromSettings();
        }

        // Every distinct size the monitor offers, plus the saved one if it isn't in the list.
        private void BuildResolutions(int savedW, int savedH)
        {
            m_Resolutions.Clear();
            m_ResolutionNames.Clear();
            foreach (var r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (!m_Resolutions.Contains(size)) m_Resolutions.Add(size);
            }
            var saved = new Vector2Int(savedW, savedH);
            if (savedW > 0 && savedH > 0 && !m_Resolutions.Contains(saved)) m_Resolutions.Add(saved);
            m_Resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            foreach (var size in m_Resolutions) m_ResolutionNames.Add($"{size.x} x {size.y}");
        }

        private int IndexOfResolution(int w, int h)
        {
            int i = m_Resolutions.IndexOf(new Vector2Int(w, h));
            return i >= 0 ? i : m_Resolutions.Count - 1;
        }
    }
}
