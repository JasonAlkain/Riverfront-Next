using UnityEngine;

namespace Game.Conquest.Audio
{
    /// <summary>
    /// How a character's sounds are heard. Your own character plays in 2D (it is always at your
    /// ears); everyone else's is a 3D sound that fades with distance, so remote players' guns,
    /// footsteps and hurt sounds come from where they are instead of filling the whole map.
    /// </summary>
    public static class ConquestAudioSetup
    {
        /// <summary>Placeholder: distance in metres at which another character's sounds start to fade
        /// (logarithmic: half as loud at twice this distance, so 10 m keeps fights 40 m away audible).</summary>
        public const float RemoteMinDistance = 10f;
        /// <summary>Placeholder: distance in metres beyond which another character's sounds stop fading.</summary>
        public const float RemoteMaxDistance = 120f;

        /// <param name="localView">True for the player's own character on their own screen.</param>
        /// <param name="localPriority">Voice priority for the local player (0 = most important, 256 = least).</param>
        public static void ConfigureCharacterSource(AudioSource source, bool localView, int localPriority = 64)
        {
            if (source == null) return;
            if (localView)
            {
                source.spatialBlend = 0f;
                source.priority = localPriority;
            }
            else
            {
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = RemoteMinDistance;
                source.maxDistance = RemoteMaxDistance;
                source.priority = 128;
            }
        }

        /// <summary>A copy of <paramref name="template"/>'s routing and 3D settings on <paramref name="host"/>.</summary>
        public static AudioSource CloneSource(AudioSource template, GameObject host)
        {
            var copy = host.AddComponent<AudioSource>();
            copy.playOnAwake = false;
            copy.outputAudioMixerGroup = template.outputAudioMixerGroup;
            copy.spatialBlend = template.spatialBlend;
            copy.rolloffMode = template.rolloffMode;
            copy.minDistance = template.minDistance;
            copy.maxDistance = template.maxDistance;
            copy.dopplerLevel = template.dopplerLevel;
            copy.spread = template.spread;
            copy.priority = template.priority;
            copy.volume = template.volume;
            return copy;
        }
    }
}
