using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Game.Conquest.Settings
{
    /// <summary>
    /// Loads, applies and saves the player's settings. Each group is applied only when it changed,
    /// so a volume tweak never resets the window or the quality level.
    /// </summary>
    public class ConquestSettingsManager : MonoBehaviour
    {
        public static ConquestSettingsManager Instance { get; private set; }

        private const string PREFS_KEY = "Conquest_GameSettings_v1";

        [Header("Audio Mixer Reference")]
        [SerializeField] private AudioMixer defaultMixer;

        private ConquestSettingsData m_CurrentSettings = new ConquestSettingsData();
        // What was last pushed to the screen and quality settings; null until the first apply.
        private ConquestSettingsData m_Applied;

        /// <summary>The live settings. Treat as read-only: change a Clone() and pass it to ApplySettings.</summary>
        public ConquestSettingsData CurrentSettings => m_CurrentSettings;

        /// <summary>Colour-blind team palette on (orange vs blue). False before settings exist.</summary>
        public static bool ColorBlindTeams => Instance != null && Instance.m_CurrentSettings.colorBlindTeams;
        public static bool ReduceMotion => Instance != null && Instance.m_CurrentSettings.reduceMotion;
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

        // AudioMixer.SetFloat is ignored during Awake, so the saved volumes are applied again here.
        private void Start()
        {
            if (Instance == this && m_CurrentSettings != null) ApplyAudioVolumes(m_CurrentSettings);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void LoadSettings()
        {
            ConquestSettingsData loaded = null;
            if (PlayerPrefs.HasKey(PREFS_KEY))
            {
                try
                {
                    loaded = JsonUtility.FromJson<ConquestSettingsData>(PlayerPrefs.GetString(PREFS_KEY));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ConquestSettingsManager] Saved settings unreadable, using defaults: {e.Message}");
                }
            }
            m_CurrentSettings = loaded ?? CreateDefaults();
        }

        public void SaveSettings()
        {
            try
            {
                PlayerPrefs.SetString(PREFS_KEY, JsonUtility.ToJson(m_CurrentSettings));
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ConquestSettingsManager] Failed to save settings: {e.Message}");
            }
        }

        public void ApplySettings(ConquestSettingsData newSettings)
        {
            var s = newSettings != null ? newSettings.Clone() : CreateDefaults();
            s.qualityLevel = ClampQuality(s.qualityLevel);

            if (s.DisplayDiffers(m_Applied) && s.resolutionWidth > 0 && s.resolutionHeight > 0)
            {
                Screen.SetResolution(s.resolutionWidth, s.resolutionHeight, ToFullScreenMode(s.displayMode));
            }
            if (m_Applied == null || s.qualityLevel != m_Applied.qualityLevel)
            {
                // Expensive changes off: only the level switches, the frame keeps going.
                QualitySettings.SetQualityLevel(s.qualityLevel, false);
            }
            // Setting the quality level resets vSync to the level's own value, so these follow it.
            QualitySettings.vSyncCount = s.vSync ? 1 : 0;
            Application.targetFrameRate = s.targetFrameRate > 0 ? s.targetFrameRate : -1;

            ApplyAudioVolumes(s);

            m_CurrentSettings = s;
            m_Applied = s.Clone();
            SaveSettings();
            OnSettingsChanged?.Invoke(m_CurrentSettings);
        }

        private void ApplyAudioVolumes(ConquestSettingsData s)
        {
            ApplyAudioVolume("MasterVolume", s.masterVolume);
            ApplyAudioVolume("MusicVolume", s.musicVolume);
            ApplyAudioVolume("SFXVolume", s.sfxVolume);
        }

        public void RestoreDefaults()
        {
            ApplySettings(CreateDefaults());
        }

        public static FullScreenMode ToFullScreenMode(int displayMode)
        {
            switch (displayMode)
            {
                case 0: return FullScreenMode.ExclusiveFullScreen;
                case 2: return FullScreenMode.Windowed;
                default: return FullScreenMode.FullScreenWindow;
            }
        }

        private static ConquestSettingsData CreateDefaults()
        {
            var s = new ConquestSettingsData();
            s.resolutionWidth = Screen.currentResolution.width;
            s.resolutionHeight = Screen.currentResolution.height;
            s.qualityLevel = QualitySettings.GetQualityLevel();
            return s;
        }

        // The project may define fewer levels than older saves expect; never skip silently.
        private static int ClampQuality(int level)
        {
            int count = QualitySettings.names.Length;
            if (level < 0 || count == 0) return QualitySettings.GetQualityLevel();
            return Mathf.Clamp(level, 0, count - 1);
        }

        private void ApplyAudioVolume(string paramName, float linearVol)
        {
            if (defaultMixer == null) return;

            // Linear to decibels: 0 -> -80 dB, 1 -> 0 dB
            float clamped = Mathf.Clamp(linearVol, 0.0001f, 1.0f);
            defaultMixer.SetFloat(paramName, Mathf.Log10(clamped) * 20.0f);
        }
    }
}
