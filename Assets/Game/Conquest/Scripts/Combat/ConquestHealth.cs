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
        [Tooltip("Seconds of invulnerability after spawning. Firing or deploying a station ends it early.")]
        [SerializeField] private float spawnProtectionSeconds = 2f;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip hitSoundClip;
        [SerializeField] private AudioClip deathSoundClip;
        [SerializeField] private AudioClip respawnSoundClip;

        private ConquestPlayerAnimator m_Animator;
        private float m_ServerProtectedUntil;

        public NetworkVariable<int> currentHealth = new NetworkVariable<int>(
            100,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>
        /// NetworkObjectId of whoever dealt the killing blow (NoAttacker for falls and holds).
        /// Declared before isDead on purpose: NGO applies a behaviour's changed variables in
        /// declaration order, so clients already have the killer when isDead's callback runs.
        /// </summary>
        public NetworkVariable<ulong> lastAttackerId = new NetworkVariable<ulong>(
            ulong.MaxValue,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> isDead = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>Attacker id for deaths with no shooter (falls, kill volumes).</summary>
        public const ulong NoAttacker = ulong.MaxValue;

        public event Action<int, int> OnHealthChanged;
        /// <summary>Raised once per death on every peer, with the attacker's NetworkObjectId
        /// (or <see cref="NoAttacker"/>). A hold for deploy also raises it, with NoAttacker.</summary>
        public event Action<ulong> OnDied;
        public event Action OnRespawned;
        /// <summary>Server only: raised when a shooter hurts this player, with the shooter's
        /// NetworkObjectId and where the shot came from. Not raised for falls.</summary>
        public event Action<ulong, Vector3> OnServerDamaged;

        public int MaxHealth => maxHealth;
        public int CurrentHealth => currentHealth.Value;
        public bool IsDead => isDead.Value;
        public ulong LastAttackerId => lastAttackerId.Value;

        private void Awake()
        {
            m_Animator = GetComponent<ConquestPlayerAnimator>();
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
        }

        public override void OnNetworkSpawn()
        {
            currentHealth.OnValueChanged += HandleHealthChanged;
            isDead.OnValueChanged += HandleDeathChanged;

            // isDead is left alone: the player may already be held out of play for deploy
            // (ServerHold), depending on which component's OnNetworkSpawn ran first.
            if (IsServer)
            {
                currentHealth.Value = maxHealth;
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
                if (!IsServer) OnDied?.Invoke(lastAttackerId.Value);
                // A hold for deploy keeps full health; only a real death plays the sound.
                if (currentHealth.Value <= 0) PlayAudio(deathSoundClip, 0.9f);
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
            TakeDamage(damage, attackerObjectId, transform.position);
        }

        /// <param name="sourcePosition">Where the damage came from (the shot's origin), for the victim's hit-direction indicator.</param>
        public void TakeDamage(int damage, ulong attackerObjectId, Vector3 sourcePosition)
        {
            TakeDamage(damage, attackerObjectId, sourcePosition, transform.position, 0, NoPellet);
        }

        /// <summary>No bolt pellet (a fall, a kill volume): clients have no drawn bolt to end.</summary>
        public const byte NoPellet = 255;

        /// <param name="hitPoint">Where the bolt struck, so every client ends its drawn bolt there.</param>
        /// <param name="shotSeq">The shooter's shot number and <paramref name="pellet"/> identify the drawn bolt.</param>
        public void TakeDamage(int damage, ulong attackerObjectId, Vector3 sourcePosition, Vector3 hitPoint, ushort shotSeq, byte pellet)
        {
            if (!IsServer || isDead.Value) return;
            // Nobody gets hurt once the match is decided.
            if (ConquestMatchManager.Instance != null && ConquestMatchManager.Instance.IsMatchOver) return;
            // Spawn protection: a fresh spawn can't be camped. Falls still kill (a protected
            // player could otherwise drop forever below the map).
            if (Time.time < m_ServerProtectedUntil && attackerObjectId != NoAttacker) return;

            int newHealth = Mathf.Max(0, currentHealth.Value - damage);
            currentHealth.Value = newHealth;

            TakeDamageClientRpc(damage, attackerObjectId, sourcePosition, hitPoint, shotSeq, pellet);
            if (attackerObjectId != NoAttacker) OnServerDamaged?.Invoke(attackerObjectId, sourcePosition);

            if (newHealth <= 0)
            {
                lastAttackerId.Value = attackerObjectId;
                isDead.Value = true;
                OnDied?.Invoke(attackerObjectId);
            }
        }

        [ClientRpc]
        private void TakeDamageClientRpc(int damage, ulong attackerObjectId, Vector3 sourcePosition, Vector3 hitPoint, ushort shotSeq, byte pellet)
        {
            if (attackerObjectId != NoAttacker && pellet != NoPellet) ConquestBoltPool.EndPellet(attackerObjectId, shotSeq, pellet, hitPoint);

            // Play subtle hurt sound with anti-stacking limiter
            ConquestAudioLimiter.TryPlayDamageHurt(audioSource, hitSoundClip, GetHashCode(), 0.18f);

            // Trigger hit reaction animation
            m_Animator?.TriggerHit();

            // Only the hurt human sees where it came from; falls have no direction.
            if (attackerObjectId != NoAttacker && ConquestPlayer.Local != null && ConquestPlayer.Local.Health == this)
            {
                Game.Conquest.UI.ConquestHUD.ShowDamageFrom(sourcePosition);
            }
        }

        private void PlayAudio(AudioClip clip, float volume = 1.0f)
        {
            if (audioSource != null && clip != null && audioSource.isActiveAndEnabled)
            {
                audioSource.pitch = UnityEngine.Random.Range(0.96f, 1.04f);
                audioSource.PlayOneShot(clip, volume);
            }
        }

        /// <summary>Server: restore up to <paramref name="amount"/> health, never above max. The dead can't be healed.</summary>
        public void Heal(int amount)
        {
            if (!IsServer || isDead.Value || amount <= 0) return;
            int newHealth = Mathf.Min(maxHealth, currentHealth.Value + amount);
            if (newHealth != currentHealth.Value) currentHealth.Value = newHealth;
        }

        public void SetMaxHealth(int newMaxHealth)
        {
            maxHealth = newMaxHealth;
            if (IsServer)
            {
                currentHealth.Value = maxHealth;
            }
        }

        /// <summary>
        /// Server: keep this player out of play, as if dead, without it counting as a death
        /// (no ticket, no killer). Used while a player picks a class and post before deploying.
        /// </summary>
        public void ServerHold()
        {
            if (!IsServer) return;
            currentHealth.Value = maxHealth;
            lastAttackerId.Value = NoAttacker;
            isDead.Value = true;
        }

        /// <summary>Server: the player has started fighting, so spawn protection no longer applies.</summary>
        public void EndSpawnProtection()
        {
            m_ServerProtectedUntil = 0f;
        }

        public void ResetHealth()
        {
            if (!IsServer) return;
            currentHealth.Value = maxHealth;
            isDead.Value = false;
            m_ServerProtectedUntil = Time.time + spawnProtectionSeconds;
            OnRespawned?.Invoke();
        }
    }
}
