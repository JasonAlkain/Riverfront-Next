using UnityEngine;

namespace Game.Conquest.Objectives
{
    /// <summary>Capture maths shared by every command post. Pure functions, so they are unit tested.</summary>
    public static class ConquestCaptureRules
    {
        /// <summary>
        /// How many times faster than one player a group of <paramref name="players"/> captures.
        /// Up to <paramref name="crowdSize"/> players, each one adds <paramref name="falloff"/> times
        /// what the one before added (1, 1 + f, 1 + f + f², ...). Every player past that adds a flat
        /// <paramref name="tailGain"/>, so a big squad still helps a little and there is no cap.
        /// </summary>
        public static float CrowdMultiplier(int players, float falloff, int crowdSize, float tailGain)
        {
            if (players <= 0) return 0f;
            float f = Mathf.Clamp(falloff, 0f, 1f);
            int crowd = Mathf.Max(1, crowdSize);
            int counted = Mathf.Min(players, crowd);

            float multiplier = f >= 1f ? counted : (1f - Mathf.Pow(f, counted)) / (1f - f);
            return multiplier + Mathf.Max(0, players - crowd) * Mathf.Max(0f, tailGain);
        }
    }
}
