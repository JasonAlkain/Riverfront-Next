using UnityEngine;
using UnityEngine.Audio;

namespace Game.Conquest.Audio
{
    /// <summary>
    /// One 2D source for interface sounds (button clicks), kept across scenes. It ignores the
    /// listener pause, so the pause menu still clicks.
    /// </summary>
    public static class ConquestUIAudio
    {
        private static AudioSource s_Source;

        // Domain reload is off: forget the source of the previous play session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Source = null;
        }

        public static void Play(AudioClip clip, float volume, AudioMixerGroup group)
        {
            if (clip == null) return;
            if (s_Source == null)
            {
                var go = new GameObject("ConquestUIAudio");
                Object.DontDestroyOnLoad(go);
                s_Source = go.AddComponent<AudioSource>();
                s_Source.playOnAwake = false;
                s_Source.spatialBlend = 0f;
                s_Source.ignoreListenerPause = true;
                s_Source.priority = 32;
            }
            if (group != null) s_Source.outputAudioMixerGroup = group;
            s_Source.PlayOneShot(clip, Mathf.Clamp01(volume));
        }
    }
}
