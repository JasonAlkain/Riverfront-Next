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

        /// <summary>Attacker id for deaths with no shooter (falls, kill volumes).</summary>
        public const ulong NoAttacker = ulong.MaxValue;

        public event Action<int, int> OnHealthChanged;
        /// <summary>Raised once per death. On the server the argument is the attacker's NetworkObjectId
        /// (or <see cref="NoAttacker"/>); clients don't know the attacker and receive NoAttacker.</summary>
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
            // NGO also invokes OnValueChanged on the server when it writes the value, and the
            // server already raises these events itself in TakeDamage / ResetHealth. Raising them
            // here too used to count every death twice (two tickets, two respawn coroutines).
            if (newVal)
            {
                if (!IsServer) OnDied?.Invoke(NoAttacker);
                PlayAudio(deathSoundClip, 0.9f);
            }
            else
            {
                if (!IsServer) OnRespawned?.Invoke();
                PlayAudio(respawnSoundClip, 0.7f);
            }
        }

        /// <param name="attackerObjectId">NetworkObjectId of the shooter (player or bot), or NoAttacker.</param>
        public void TakeDamage(int damage, ulong attackerObjectId)
        {
            if (!IsServer || isDead.Value) return;

            int newHealth = Mathf.Max(0, currentHealth.Value - damage);
            currentHealth.Value = newHealth;

            TakeDamageClientRpc(damage, attackerObjectId);

            if (newHealth <= 0)
            {
                isDead.Value = true;
                OnDied?.Invoke(attackerObjectId);
            }
        }

        [ClientRpc]
        private void TakeDamageClientRpc(int damage, ulong attackerObjectId)
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
