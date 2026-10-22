using UnityEngine;

namespace Game.Conquest.Objectives
{
    /// <summary>Capture maths shared by every command post. Pure functions, so they are unit tested.</summary>
    public static class ConquestCaptureRules
    {
        /// <summary>
        /// How many times faster than one player a group of <paramref name="players"/> captures.
        /// Each extra player adds <paramref name="falloff"/> times what the one before added
        /// (1, 1 + f, 1 + f + f², ...), so the first few players matter and a big crowd adds
        /// little, but there is no hard cap. The total never reaches 1 / (1 - f).
        /// </summary>
        public static float CrowdMultiplier(int players, float falloff)
        {
            if (players <= 0) return 0f;
            float f = Mathf.Clamp(falloff, 0f, 0.95f);
            if (f <= 0f) return 1f;
            return (1f - Mathf.Pow(f, players)) / (1f - f);
        }
    }
}
