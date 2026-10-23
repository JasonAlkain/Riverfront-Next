using UnityEngine;
using Game.Conquest.Audio;

namespace Game.Conquest.Player
{
    /// <summary>
    /// Receiver for the animation events the imported clips call, so Unity finds a method for
    /// each one. Footsteps and landings are played by ConquestFootsteps from real movement, and
    /// reload sounds by ConquestWeapon (each weapon's own clip), so these events play nothing.
    /// The source and clips here are the ones ConquestFootsteps uses.
    /// </summary>
    public class ConquestAnimationEvents : MonoBehaviour
    {
        [Header("Footstep Audio (Optional)")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip footstepClip;
        [SerializeField] private AudioClip landClip;
        [Tooltip("Unused: the weapon plays its own reload sound.")]
        [SerializeField] private AudioClip reloadClip;

        public AudioSource AudioSource => audioSource;

        // Called by animation clips such as Run_N, Walk_N
        public void OnCharEvent(AnimationEvent e) { }
        public void OnCharEvent() { }

        // Called on landing animations
        public void OnLand(AnimationEvent e) { }
        public void OnLand() { }

        // Called on reload animations
        public void ReloadAnimationSFXTrigger(AnimationEvent e) { }
        public void ReloadAnimationSFXTrigger() { }

        // Generic callbacks that other templates or clips might trigger
        public void OnFootDown(AnimationEvent e) { }
        public void OnFootDown() { }
        public void OnJumpStart(AnimationEvent e) { }
        public void OnJumpStart() { }
        public void OnDoubleJump(AnimationEvent e) { }
        public void OnDoubleJump() { }

        public void PlayFootstep(float volume)
        {
            if (audioSource != null && audioSource.isActiveAndEnabled && footstepClip != null)
            {
                ConquestAudioLimiter.TryPlayFootstep(audioSource, footstepClip, GetHashCode(), volume);
            }
        }

        public void PlayLand(float volume)
        {
            if (audioSource != null && audioSource.isActiveAndEnabled && landClip != null)
            {
                ConquestAudioLimiter.TryPlayOneShot(audioSource, landClip, "Land", volume, 0.3f);
            }
        }
    }
}
