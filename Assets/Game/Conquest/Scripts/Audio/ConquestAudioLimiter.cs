using System.Collections.Generic;
using UnityEngine;

namespace Game.Conquest.Audio
{
    /// <summary>
    /// Global audio limiter to prevent acoustic clipping, sound stacking, and cacophony from concurrent weapon/hit audio.
    /// A category allows a few voices per interval rather than one, so two players firing at once are both heard,
    /// and a priority sound (the local player's own weapon) is never dropped by the category.
    /// </summary>
    public static class ConquestAudioLimiter
    {
        private struct Window
        {
            public float start;
            public int count;
        }

        // Entries older than this are dropped once a dictionary grows past PruneThreshold, so
        // despawned sources and one-off categories don't accumulate over a long session.
        private const float StaleSeconds = 2f;
        private const int PruneThreshold = 256;

        private static readonly Dictionary<string, Window> s_Categories = new Dictionary<string, Window>();
        private static readonly Dictionary<int, float> s_SourceLastPlayTime = new Dictionary<int, float>();
        private static readonly List<int> s_StaleSources = new List<int>();
        private static readonly List<string> s_StaleCategories = new List<string>();

        // Domain reload is off, so the tables would otherwise survive between play sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Categories.Clear();
            s_SourceLastPlayTime.Clear();
            s_StaleSources.Clear();
            s_StaleCategories.Clear();
        }

        /// <summary>
        /// Attempts to play a sound on an AudioSource with per-source and category limits.
        /// </summary>
        /// <param name="maxPerInterval">Voices the category may start within <paramref name="minInterval"/>.</param>
        /// <param name="priority">Ignores the category limit (still counts toward it) and the per-source cooldown.</param>
        public static bool TryPlayOneShot(AudioSource source, AudioClip clip, string category = "", float volume = 1.0f, float minInterval = 0.05f,
                                          float pitchMin = 0.98f, float pitchMax = 1.02f, int maxPerInterval = 1, bool priority = false)
        {
            if (!Admit(source, clip, category, minInterval, maxPerInterval, priority)) return false;

            // 3. Play with slight pitch randomization to prevent phasing artifacts
            source.pitch = Random.Range(pitchMin, pitchMax);
            source.PlayOneShot(clip, Mathf.Clamp01(volume));
            return true;
        }

        /// <summary>
        /// Like <see cref="TryPlayOneShot"/>, but restarts <paramref name="source"/> with the clip (cutting
        /// whatever it was playing). Rotating through a few such sources caps how many voices one
        /// rapid-fire weapon can hold, and the pitch change can't retune an earlier shot's tail.
        /// </summary>
        public static bool TryPlayVoice(AudioSource source, AudioClip clip, string category, float volume, float minInterval,
                                        float pitchMin, float pitchMax, int maxPerInterval, bool priority)
        {
            if (!Admit(source, clip, category, minInterval, maxPerInterval, priority)) return false;
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = Random.Range(pitchMin, pitchMax);
            source.Play();
            return true;
        }

        private static bool Admit(AudioSource source, AudioClip clip, string category, float minInterval, int maxPerInterval, bool priority)
        {
            if (source == null || clip == null || !source.isActiveAndEnabled) return false;

            float now = Time.time;

            // 1. Category budget (prevents a wall of identical sounds from many entities at once).
            if (!string.IsNullOrEmpty(category))
            {
                s_Categories.TryGetValue(category, out Window window);
                if (now - window.start >= minInterval)
                {
                    window.start = now;
                    window.count = 0;
                }
                if (!priority && window.count >= Mathf.Max(1, maxPerInterval)) return false;
                window.count++;
                s_Categories[category] = window;
                if (s_Categories.Count > PruneThreshold) PruneCategories(now);
            }

            // 2. Per-source cooldown (prevents one AudioSource from spamming sounds in the same frame).
            int sourceId = source.GetHashCode(); // a UnityEngine.Object hashes by its instance id
            if (!priority && s_SourceLastPlayTime.TryGetValue(sourceId, out float lastSourceTime) && now - lastSourceTime < 0.03f)
            {
                return false; // Max ~33 sounds/sec per source
            }
            s_SourceLastPlayTime[sourceId] = now;
            if (s_SourceLastPlayTime.Count > PruneThreshold) PruneSources(now);
            return true;
        }

        private static void PruneSources(float now)
        {
            s_StaleSources.Clear();
            foreach (var pair in s_SourceLastPlayTime)
            {
                if (now - pair.Value > StaleSeconds) s_StaleSources.Add(pair.Key);
            }
            for (int i = 0; i < s_StaleSources.Count; i++) s_SourceLastPlayTime.Remove(s_StaleSources[i]);
        }

        private static void PruneCategories(float now)
        {
            s_StaleCategories.Clear();
            foreach (var pair in s_Categories)
            {
                if (now - pair.Value.start > StaleSeconds) s_StaleCategories.Add(pair.Key);
            }
            for (int i = 0; i < s_StaleCategories.Count; i++) s_Categories.Remove(s_StaleCategories[i]);
        }

        /// <summary>
        /// Specific limiter for hit feedback audio (subtle and strictly rate-limited).
        /// </summary>
        public static bool TryPlayHitFeedback(AudioSource source, AudioClip clip, float volume = 0.18f, bool kill = false)
        {
            // A kill always plays, even right after a hit confirm.
            return TryPlayOneShot(source, clip, "HitFeedback", volume, 0.08f, 0.98f, 1.02f, 1, kill);
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
