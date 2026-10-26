using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Gameplay;
using Game.Conquest.Player;

namespace Game.Conquest.Combat
{
    public class ConquestSupportStation : NetworkBehaviour
    {
        [Header("Station Settings")]
        [SerializeField] private float radius = 6.0f;
        [SerializeField] private int healAmount = 15;
        [SerializeField] private float pulseInterval = 1.5f;
        [SerializeField] private float lifetime = 45.0f;
        [SerializeField] private int maxHealth = 80;

        [Header("Visuals")]
        [SerializeField] private Renderer stationRenderer;
        [Tooltip("Pooled effect played on every client when the station is destroyed (not when it expires).")]
        [SerializeField] private GameObject destroyEffect;
        [SerializeField] private float destroyEffectSeconds = 3f;
        [SerializeField] private Material redMaterial;
        [SerializeField] private Material blueMaterial;

        public NetworkVariable<Team> stationTeam = new NetworkVariable<Team>(
            Team.Neutral,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<int> currentHealth = new NetworkVariable<int>(
            80,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private static readonly List<ConquestSupportStation> s_Active = new List<ConquestSupportStation>();

        private float m_NextPulseTime;
        private float m_ServerExpiresAt;
        // Server: destroyed, and gone once the explosion RPC has had a moment to reach everyone.
        private bool m_Destroyed;
        private float m_DespawnAt;
        private const float DespawnDelay = 0.25f;

        public float Radius => radius;

        /// <summary>Spawned stations on this peer (the server's list is the one rules use).</summary>
        public static IReadOnlyList<ConquestSupportStation> Active => s_Active;

        public static int CountForTeam(Team team)
        {
            int count = 0;
            for (int i = 0; i < s_Active.Count; i++)
            {
                if (s_Active[i] != null && s_Active[i].stationTeam.Value == team) count++;
            }
            return count;
        }

        public override void OnNetworkSpawn()
        {
            stationTeam.OnValueChanged += HandleTeamChanged;
            UpdateVisuals(stationTeam.Value);
            if (!s_Active.Contains(this)) s_Active.Add(this);

            if (IsClient) ConquestFxPool.Prewarm(destroyEffect, 1);
            if (IsServer)
            {
                currentHealth.Value = maxHealth;
                m_NextPulseTime = Time.time + pulseInterval;
                m_ServerExpiresAt = Time.time + lifetime;
            }
        }

        public override void OnNetworkDespawn()
        {
            stationTeam.OnValueChanged -= HandleTeamChanged;
            s_Active.Remove(this);
        }

        private void HandleTeamChanged(Team oldTeam, Team newTeam)
        {
            UpdateVisuals(newTeam);
        }

        public void UpdateVisuals(Team team)
        {
            if (stationRenderer == null) return;
            Material mat = team == Team.Red ? redMaterial : (team == Team.Blue ? blueMaterial : null);
            if (mat != null) stationRenderer.sharedMaterial = mat;
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned) return;

            if (m_Destroyed)
            {
                if (Time.time >= m_DespawnAt) NetworkObject.Despawn(true);
                return;
            }

            // Despawn (not Destroy) so the station disappears on every peer and NGO stays consistent.
            if (Time.time >= m_ServerExpiresAt)
            {
                NetworkObject.Despawn(true);
                return;
            }

            if (Time.time >= m_NextPulseTime)
            {
                m_NextPulseTime = Time.time + pulseInterval;
                PulseSupport();
            }
        }

        // Each pulse heals and refills every living teammate in range, bots included.
        private void PulseSupport()
        {
            float sqrRadius = radius * radius;
            Vector3 center = transform.position;
            var all = ConquestPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                ConquestPlayer player = all[i];
                if (player == null || player.Health.IsDead || player.CurrentTeam != stationTeam.Value) continue;
                if ((player.transform.position - center).sqrMagnitude > sqrRadius) continue;

                player.Health.Heal(healAmount);
                player.ServerResupply();
            }
        }

        public void TakeDamage(int damage)
        {
            if (!IsServer || !IsSpawned || m_Destroyed) return;
            currentHealth.Value = Mathf.Max(0, currentHealth.Value - damage);
            if (currentHealth.Value <= 0)
            {
                m_Destroyed = true;
                m_DespawnAt = Time.time + DespawnDelay;
                HideStation();
                DestroyedClientRpc(transform.position);
            }
        }

        [ClientRpc]
        private void DestroyedClientRpc(Vector3 position)
        {
            HideStation();
            ConquestFxPool.Play(destroyEffect, position + Vector3.up * 0.5f, Quaternion.identity, destroyEffectSeconds);
        }

        // Out of sight and out of the way of bolts while the despawn reaches everyone.
        private void HideStation()
        {
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = stationTeam.Value == Team.Red ? Color.red : Color.blue;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
