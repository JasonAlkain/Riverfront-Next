using System;
using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Gameplay;
using Game.Conquest.Player;
using Game.Conquest.Audio;

namespace Game.Conquest.Combat
{
    public class ConquestHealth : NetworkBehaviour
    {
        [Header("Health Settings")]
        [SerializeField] private int maxHealth = 100;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip hitSoundClip;
        [SerializeField] private AudioClip deathSoundClip;
        [SerializeField] private AudioClip respawnSoundClip;

        private ConquestPlayerAnimator m_Animator;

        public NetworkVariable<int> currentHealth = new NetworkVariable<int>(
            100,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> isDead = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public event Action<int, int> OnHealthChanged;
        public event Action<ulong> OnDied;
        public event Action OnRespawned;

        public int MaxHealth => maxHealth;
        public int CurrentHealth => currentHealth.Value;
        public bool IsDead => isDead.Value;

        private void Awake()
        {
            m_Animator = GetComponent<ConquestPlayerAnimator>();
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
        }

        public override void OnNetworkSpawn()
        {
            currentHealth.OnValueChanged += HandleHealthChanged;
            isDead.OnValueChanged += HandleDeathChanged;

            if (IsServer)
            {
                currentHealth.Value = maxHealth;
                isDead.Value = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            currentHealth.OnValueChanged -= HandleHealthChanged;
            isDead.OnValueChanged -= HandleDeathChanged;
        }

        private void HandleHealthChanged(int oldVal, int newVal)
        {
            OnHealthChanged?.Invoke(newVal, maxHealth);
        }

        private void HandleDeathChanged(bool oldVal, bool newVal)
        {
            if (newVal)
            {
                OnDied?.Invoke(0);
                PlayAudio(deathSoundClip, 0.9f);
            }
            else
            {
                OnRespawned?.Invoke();
                PlayAudio(respawnSoundClip, 0.7f);
            }
        }

        public void TakeDamage(int damage, ulong attackerClientId)
        {
            if (!IsServer || isDead.Value) return;

            int newHealth = Mathf.Max(0, currentHealth.Value - damage);
            currentHealth.Value = newHealth;

            TakeDamageClientRpc(damage, attackerClientId);

            if (newHealth <= 0)
            {
                isDead.Value = true;
                OnDied?.Invoke(attackerClientId);
            }
        }

        [ClientRpc]
        private void TakeDamageClientRpc(int damage, ulong attackerClientId)
        {
            // Play subtle hurt sound with anti-stacking limiter
            ConquestAudioLimiter.TryPlayDamageHurt(audioSource, hitSoundClip, GetHashCode(), 0.18f);

            // Trigger hit reaction animation
            m_Animator?.TriggerHit();
        }

        private void PlayAudio(AudioClip clip, float volume = 1.0f)
        {
            if (audioSource != null && clip != null && audioSource.isActiveAndEnabled)
            {
                audioSource.pitch = UnityEngine.Random.Range(0.96f, 1.04f);
                audioSource.PlayOneShot(clip, volume);
            }
        }

        public void SetMaxHealth(int newMaxHealth)
        {
            maxHealth = newMaxHealth;
            if (IsServer)
            {
                currentHealth.Value = maxHealth;
            }
        }

        public void ResetHealth()
        {
            if (!IsServer) return;
            currentHealth.Value = maxHealth;
            isDead.Value = false;
            OnRespawned?.Invoke();
        }
    }
}
