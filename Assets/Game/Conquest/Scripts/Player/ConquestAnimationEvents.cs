using UnityEngine;
using Game.Conquest.Audio;

namespace Game.Conquest.Player
{
    public class ConquestAnimationEvents : MonoBehaviour
    {
        [Header("Footstep Audio (Optional)")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip footstepClip;
        [SerializeField] private AudioClip landClip;
        [SerializeField] private AudioClip reloadClip;

        // Called by animation clips such as Run_N, Walk_N
        public void OnCharEvent(AnimationEvent e)
        {
            PlayFootstep();
        }

        public void OnCharEvent()
        {
            PlayFootstep();
        }

        // Called on landing animations
        public void OnLand(AnimationEvent e)
        {
            PlayLand();
        }

        public void OnLand()
        {
            PlayLand();
        }

        // Called on reload animations
        public void ReloadAnimationSFXTrigger(AnimationEvent e)
        {
            if (audioSource != null && reloadClip != null)
            {
                audioSource.PlayOneShot(reloadClip);
            }
        }

        public void ReloadAnimationSFXTrigger()
        {
            if (audioSource != null && reloadClip != null)
            {
                audioSource.PlayOneShot(reloadClip);
            }
        }

        // Generic callbacks that other templates or clips might trigger
        public void OnFootDown(AnimationEvent e) => PlayFootstep();
        public void OnFootDown() => PlayFootstep();
        public void OnJumpStart(AnimationEvent e) { }
        public void OnJumpStart() { }
        public void OnDoubleJump(AnimationEvent e) { }
        public void OnDoubleJump() { }

        private void PlayFootstep()
        {
            if (audioSource != null && audioSource.isActiveAndEnabled && footstepClip != null)
            {
                ConquestAudioLimiter.TryPlayFootstep(audioSource, footstepClip, GetHashCode(), 0.16f);
            }
        }

        private void PlayLand()
        {
            if (audioSource != null && audioSource.isActiveAndEnabled && landClip != null)
            {
                ConquestAudioLimiter.TryPlayOneShot(audioSource, landClip, "Land", 0.35f, 0.3f);
            }
        }
    }
}
