using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Gameplay;
using Game.Conquest.Player;

namespace Game.Conquest.Combat
{
    /// <summary>What a supply droid hands out. Values are stable: they are serialized.</summary>
    public enum SupplyKind : byte
    {
        Health = 0,
        Ammo = 1
    }

    /// <summary>
    /// A map-placed health or ammo droid. Every pulse it heals (or resupplies) the living players
    /// in range that it serves. Bolts and grenades can break it; a broken droid stays where it is
    /// (it never respawns) and does nothing until an engineer's repair tool brings it back to
    /// full health. The server owns its health; every peer only draws it.
    /// </summary>
    public class ConquestSupplyDroid : NetworkBehaviour
    {
        [Header("Supply")]
        [SerializeField] private SupplyKind kind = SupplyKind.Health;
        [Tooltip("Team it serves. Neutral serves both teams, and either team can break it.")]
        [SerializeField] private Team servesTeam = Team.Neutral;
        [Tooltip("Placeholder: metres around the droid that a pulse reaches.")]
        [SerializeField] private float radius = 4f;
        [Tooltip("Placeholder: seconds between pulses.")]
        [SerializeField] private float pulseInterval = 1.5f;
        [Tooltip("Placeholder: health restored per pulse by a health droid.")]
        [SerializeField] private int healPerPulse = 12;

        [Header("Durability")]
        [Tooltip("Placeholder: damage it takes to break.")]
        [SerializeField] private int maxHealth = 150;

        [Header("Visuals")]
        [Tooltip("Tinted through a property block, so the shared material stays untouched.")]
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Color healthColor = new Color(0.3f, 0.85f, 0.4f, 1f);
        [SerializeField] private Color ammoColor = new Color(0.95f, 0.8f, 0.3f, 1f);
        [SerializeField] private Color brokenColor = new Color(0.25f, 0.25f, 0.27f, 1f);
        [Tooltip("Shown while the droid works (for example a light). Hidden while broken.")]
        [SerializeField] private GameObject workingVisual;
        [Tooltip("Pooled effect played on every client when the droid breaks.")]
        [SerializeField] private GameObject breakEffect;
        [SerializeField] private float breakEffectSeconds = 3f;

        public NetworkVariable<int> currentHealth = new NetworkVariable<int>(
            150,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private static readonly List<ConquestSupplyDroid> s_Active = new List<ConquestSupplyDroid>();
        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");

        private MaterialPropertyBlock m_Block;
        private float m_NextPulseTime;
        // Server: repair put into a broken droid so far. It stays broken until this reaches max health.
        private int m_RepairProgress;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Active.Clear();
        }

        /// <summary>Spawned droids on this peer.</summary>
        public static IReadOnlyList<ConquestSupplyDroid> Active => s_Active;

        public SupplyKind Kind => kind;
        public int MaxHealth => maxHealth;
        public bool IsBroken => currentHealth.Value <= 0;
        public bool IsDamaged => currentHealth.Value < maxHealth;
        public float Health01 => maxHealth > 0 ? Mathf.Clamp01((float)currentHealth.Value / maxHealth) : 0f;

        public override void OnNetworkSpawn()
        {
            if (IsClient) ConquestFxPool.Prewarm(breakEffect, 1);
            currentHealth.OnValueChanged += HandleHealthChanged;
            if (!s_Active.Contains(this)) s_Active.Add(this);

            if (IsServer)
            {
                currentHealth.Value = maxHealth;
                m_NextPulseTime = Time.time + pulseInterval;
            }
            UpdateVisuals();
        }

        public override void OnNetworkDespawn()
        {
            currentHealth.OnValueChanged -= HandleHealthChanged;
            s_Active.Remove(this);
        }

        private void HandleHealthChanged(int previous, int current)
        {
            // Only breaking and coming back change the look.
            if ((previous <= 0) != (current <= 0)) UpdateVisuals();
            // Every client sees the change happen (a late joiner gets the value at spawn instead, so
            // it doesn't replay an old break). A dedicated server has nothing to draw.
            if (previous > 0 && current <= 0 && IsClient)
            {
                ConquestFxPool.Play(breakEffect, transform.position + Vector3.up * 0.6f, Quaternion.identity, breakEffectSeconds);
            }
        }

        /// <summary>True if this droid serves (heals, resupplies, can be repaired by) a team.</summary>
        public bool Serves(Team team)
        {
            return team != Team.Neutral && (servesTeam == Team.Neutral || servesTeam == team);
        }

        /// <summary>Server: whether a bolt or blast from <paramref name="attackerTeam"/> hurts this droid.</summary>
        public bool CanBeDamagedBy(Team attackerTeam)
        {
            return servesTeam == Team.Neutral || attackerTeam != servesTeam;
        }

        /// <summary>Server: take damage. A broken droid takes no more.</summary>
        public void TakeDamage(int damage)
        {
            if (!IsServer || !IsSpawned || IsBroken || damage <= 0) return;
            currentHealth.Value = Mathf.Max(0, currentHealth.Value - damage);
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned) return;

            if (IsBroken || Time.time < m_NextPulseTime) return;
            m_NextPulseTime = Time.time + pulseInterval;
            ServerPulse();
        }

        private void ServerPulse()
        {
            float sqrRadius = radius * radius;
            Vector3 center = transform.position;
            var all = ConquestPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                ConquestPlayer player = all[i];
                if (player == null || player.Health == null || player.Health.IsDead || !Serves(player.CurrentTeam)) continue;
                if ((player.transform.position - center).sqrMagnitude > sqrRadius) continue;

                if (kind == SupplyKind.Health) player.Health.Heal(healPerPulse);
                else player.ServerResupply();
            }
        }

        /// <summary>Server: one use of a repair tool. A broken droid works again once it is back to full health.</summary>
        public void ServerRepair(int amount)
        {
            if (!IsServer || !IsSpawned || !IsDamaged || amount <= 0) return;
            if (!IsBroken)
            {
                currentHealth.Value = Mathf.Min(maxHealth, currentHealth.Value + amount);
                return;
            }

            // Half-repaired is still broken: it only comes back into service when fully fixed.
            m_RepairProgress += amount;
            if (m_RepairProgress < maxHealth) return;
            m_RepairProgress = 0;
            currentHealth.Value = maxHealth;
            // The first pulse waits a full interval, like a fresh droid.
            m_NextPulseTime = Time.time + pulseInterval;
        }

        private void UpdateVisuals()
        {
            bool broken = IsBroken;
            if (workingVisual != null) workingVisual.SetActive(!broken);
            if (bodyRenderer == null) return;
            if (m_Block == null) m_Block = new MaterialPropertyBlock();
            bodyRenderer.GetPropertyBlock(m_Block);
            m_Block.SetColor(s_BaseColorId, broken ? brokenColor : kind == SupplyKind.Health ? healthColor : ammoColor);
            bodyRenderer.SetPropertyBlock(m_Block);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = kind == SupplyKind.Health ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
