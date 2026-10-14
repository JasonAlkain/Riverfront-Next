using System;
using UnityEngine;

namespace Game.Conquest.Settings
{
    [Serializable]
    public class ConquestSettingsData
    {
        // Graphics
        public int resolutionWidth = 1920;
        public int resolutionHeight = 1080;
        public int refreshRate = 60;
        public int displayMode = 0; // 0=Fullscreen, 1=Borderless, 2=Windowed
        public int qualityLevel = 2; // 0=Low, 1=Medium, 2=High, 3=Ultra
        public bool vSync = true;
        public int targetFrameRate = 60; // 30, 60, 120, 144, 0=unlimited
        public float fieldOfView = 75.0f; // 60 to 100

        // Audio
        public float masterVolume = 0.85f;
        public float musicVolume = 0.70f;
        public float sfxVolume = 0.80f;

        // Controls
        public float mouseSensitivity = 0.15f;
        public bool invertY = false;

        // Accessibility & UI
        public float uiScale = 1.0f; // 0.8 to 1.3
        public bool highContrast = false;
        public bool reduceMotion = false;
    }
}
