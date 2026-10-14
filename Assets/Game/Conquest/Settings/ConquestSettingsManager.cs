using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Game.Conquest.Settings
{
    public class ConquestSettingsManager : MonoBehaviour
    {
        public static ConquestSettingsManager Instance { get; private set; }

        private const string PREFS_KEY = "Conquest_GameSettings_v1";

        [Header("Audio Mixer Reference")]
        [SerializeField] private AudioMixer defaultMixer;

        private ConquestSettingsData m_CurrentSettings = new ConquestSettingsData();

        public ConquestSettingsData CurrentSettings => m_CurrentSettings;
        public event Action<ConquestSettingsData> OnSettingsChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            LoadSettings();
            ApplySettings(m_CurrentSettings);
        }

        public void LoadSettings()
        {
            if (PlayerPrefs.HasKey(PREFS_KEY))
            {
                try
                {
                    string json = PlayerPrefs.GetString(PREFS_KEY);
                    m_CurrentSettings = JsonUtility.FromJson<ConquestSettingsData>(json);
                    if (m_CurrentSettings == null) m_CurrentSettings = new ConquestSettingsData();
                }
                catch
                {
                    m_CurrentSettings = new ConquestSettingsData();
                }
            }
            else
            {
                m_CurrentSettings = new ConquestSettingsData();
                m_CurrentSettings.resolutionWidth = Screen.currentResolution.width;
                m_CurrentSettings.resolutionHeight = Screen.currentResolution.height;
                SaveSettings();
            }
        }

        public void SaveSettings()
        {
            try
            {
                string json = JsonUtility.ToJson(m_CurrentSettings);
                PlayerPrefs.SetString(PREFS_KEY, json);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ConquestSettingsManager] Failed to save settings: {e.Message}");
            }
        }

        public void ApplySettings(ConquestSettingsData newSettings)
        {
            m_CurrentSettings = newSettings ?? new ConquestSettingsData();

            // 1. Graphics - Display Mode & Resolution
            FullScreenMode fsMode = FullScreenMode.FullScreenWindow;
            switch (m_CurrentSettings.displayMode)
            {
                case 0: fsMode = FullScreenMode.ExclusiveFullScreen; break;
                case 1: fsMode = FullScreenMode.FullScreenWindow; break;
                case 2: fsMode = FullScreenMode.Windowed; break;
            }

            if (m_CurrentSettings.resolutionWidth > 0 && m_CurrentSettings.resolutionHeight > 0)
            {
                Screen.SetResolution(m_CurrentSettings.resolutionWidth, m_CurrentSettings.resolutionHeight, fsMode);
            }

            // Quality level
            if (m_CurrentSettings.qualityLevel >= 0 && m_CurrentSettings.qualityLevel < QualitySettings.names.Length)
            {
                QualitySettings.SetQualityLevel(m_CurrentSettings.qualityLevel, true);
            }

            // VSync
            QualitySettings.vSyncCount = m_CurrentSettings.vSync ? 1 : 0;

            // Frame Rate
            Application.targetFrameRate = m_CurrentSettings.targetFrameRate > 0 ? m_CurrentSettings.targetFrameRate : -1;

            // 2. Audio - Bind to AudioMixer
            ApplyAudioVolume("MasterVolume", m_CurrentSettings.masterVolume);
            ApplyAudioVolume("MusicVolume", m_CurrentSettings.musicVolume);
            ApplyAudioVolume("SFXVolume", m_CurrentSettings.sfxVolume);

            // 3. Save
            SaveSettings();
            OnSettingsChanged?.Invoke(m_CurrentSettings);
        }

        private void ApplyAudioVolume(string paramName, float linearVol)
        {
            if (defaultMixer == null) return;

            // Linear to decibels: 0 -> -80 dB, 1 -> 0 dB
            float clamped = Mathf.Clamp(linearVol, 0.0001f, 1.0f);
            float dB = Mathf.Log10(clamped) * 20.0f;
            defaultMixer.SetFloat(paramName, dB);
        }

        public void RestoreDefaults()
        {
            m_CurrentSettings = new ConquestSettingsData();
            m_CurrentSettings.resolutionWidth = Screen.currentResolution.width;
            m_CurrentSettings.resolutionHeight = Screen.currentResolution.height;
            ApplySettings(m_CurrentSettings);
        }
    }
}
