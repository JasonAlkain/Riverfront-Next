using System.Collections.Generic;
using UnityEngine;

namespace Game.Conquest.Audio
{
    /// <summary>
    /// Global audio limiter to prevent acoustic clipping, sound stacking, and cacophony from concurrent weapon/hit audio.
    /// </summary>
    public static class ConquestAudioLimiter
    {
        private static readonly Dictionary<string, float> s_CategoryLastPlayTime = new Dictionary<string, float>();
        private static readonly Dictionary<int, float> s_SourceLastPlayTime = new Dictionary<int, float>();

        /// <summary>
        /// Attempts to play a sound on an AudioSource with per-source and global category cooldowns.
        /// </summary>
        public static bool TryPlayOneShot(AudioSource source, AudioClip clip, string category = "", float volume = 1.0f, float minInterval = 0.05f, float pitchMin = 0.98f, float pitchMax = 1.02f)
        {
            if (source == null || clip == null || !source.isActiveAndEnabled) return false;

            float now = Time.time;

            // 1. Check category-based cooldown (prevents overlapping identical sounds from multiple entities)
            if (!string.IsNullOrEmpty(category))
            {
                if (s_CategoryLastPlayTime.TryGetValue(category, out float lastCategoryTime))
                {
                    if (now - lastCategoryTime < minInterval)
                    {
                        return false;
                    }
                }
                s_CategoryLastPlayTime[category] = now;
            }

            // 2. Check per-source cooldown (prevents one AudioSource from spamming sounds in the same frame)
            int sourceId = source.GetHashCode();
            if (s_SourceLastPlayTime.TryGetValue(sourceId, out float lastSourceTime))
            {
                if (now - lastSourceTime < 0.03f) // Max ~33 sounds/sec per source
                {
                    return false;
                }
            }
            s_SourceLastPlayTime[sourceId] = now;

            // 3. Play with slight pitch randomization to prevent phasing artifacts
            source.pitch = Random.Range(pitchMin, pitchMax);
            source.PlayOneShot(clip, Mathf.Clamp01(volume));
            return true;
        }

        /// <summary>
        /// Specific limiter for hit feedback audio (subtle and strictly rate-limited).
        /// </summary>
        public static bool TryPlayHitFeedback(AudioSource source, AudioClip clip, float volume = 0.18f)
        {
            return TryPlayOneShot(source, clip, "HitFeedback", volume, 0.08f, 0.98f, 1.02f);
        }

        /// <summary>
        /// Specific limiter for damage hurt audio (strictly rate-limited per victim).
        /// </summary>
        public static bool TryPlayDamageHurt(AudioSource source, AudioClip clip, int victimId, float volume = 0.20f)
        {
            string key = $"Damage_{victimId}";
            return TryPlayOneShot(source, clip, key, volume, 0.22f, 0.96f, 1.04f);
        }

        /// <summary>
        /// Specific limiter for footstep sounds.
        /// </summary>
        public static bool TryPlayFootstep(AudioSource source, AudioClip clip, int entityId, float volume = 0.18f)
        {
            string key = $"Footstep_{entityId}";
            return TryPlayOneShot(source, clip, key, volume, 0.28f, 0.95f, 1.05f);
        }
    }
}
