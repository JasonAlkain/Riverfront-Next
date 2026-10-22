using System;
using UnityEngine;

namespace Game.Conquest.Settings
{
    [Serializable]
    public class ConquestSettingsData
    {
        // Display
        public int resolutionWidth = 1920;
        public int resolutionHeight = 1080;
        public int refreshRate = 60;
        public int displayMode = 1; // 0=Fullscreen, 1=Borderless, 2=Windowed
        public int qualityLevel = -1; // index into QualitySettings.names; -1 = the project's default level
        public bool vSync = true;
        public int targetFrameRate = 0; // 30, 60, 120, 144, 0=unlimited
        public float fieldOfView = 75.0f; // 60 to 100

        // Audio
        public float masterVolume = 0.85f;
        public float musicVolume = 0.70f;
        public float sfxVolume = 0.80f;

        // Controls
        public float mouseSensitivity = 0.15f;
        [Tooltip("Multiplier on top of the automatic zoom scaling while aiming down a scope.")]
        public float zoomSensitivity = 1.0f; // 0.5 to 1.5
        public bool invertY = false;

        // Accessibility & UI
        public float uiScale = 1.0f; // 0.8 to 1.3
        public bool highContrast = false;
        public bool reduceMotion = false;
        public bool colorBlindTeams = false;

        public ConquestSettingsData Clone() => (ConquestSettingsData)MemberwiseClone();

        /// <summary>True when applying this over <paramref name="other"/> needs a resolution or window-mode change.</summary>
        public bool DisplayDiffers(ConquestSettingsData other)
        {
            return other == null || resolutionWidth != other.resolutionWidth || resolutionHeight != other.resolutionHeight ||
                   displayMode != other.displayMode;
        }
    }
}
