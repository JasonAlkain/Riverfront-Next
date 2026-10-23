using UnityEngine;

namespace Game.Conquest.Audio
{
    /// <summary>
    /// Gives an AudioSource a committed clip when its own is missing. Some sounds come from the
    /// Free SFX Pack, which stays out of the public repository (Asset Store licence), so a fresh
    /// clone has no clip there; this keeps the effect audible instead of silent.
    /// </summary>
    public class ConquestAudioFallback : MonoBehaviour
    {
        [SerializeField] private AudioSource source;
        [Tooltip("A committed clip, used only while the source's own clip is missing.")]
        [SerializeField] private AudioClip fallbackClip;

        private void Awake()
        {
            if (source == null) source = GetComponent<AudioSource>();
            if (source != null && source.clip == null) source.clip = fallbackClip;
        }
    }
}
