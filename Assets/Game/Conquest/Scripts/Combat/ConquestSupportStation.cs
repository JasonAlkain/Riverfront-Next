using System.Collections;
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

        private float m_NextPulseTime;

        public override void OnNetworkSpawn()
        {
            stationTeam.OnValueChanged += HandleTeamChanged;
            UpdateVisuals(stationTeam.Value);

            if (IsServer)
            {
                currentHealth.Value = maxHealth;
                m_NextPulseTime = Time.time + pulseInterval;
                Destroy(gameObject, lifetime);
            }
        }

        public override void OnNetworkDespawn()
        {
            stationTeam.OnValueChanged -= HandleTeamChanged;
        }

        private void HandleTeamChanged(Team oldTeam, Team newTeam)
        {
            UpdateVisuals(newTeam);
        }

        public void UpdateVisuals(Team team)
        {
            if (stationRenderer == null) return;
            Material mat = team == Team.Red ? redMaterial : (team == Team.Blue ? blueMaterial : null);
            if (mat != null) stationRenderer.material = mat;
        }

        private void Update()
        {
            if (!IsServer) return;

            if (Time.time >= m_NextPulseTime)
            {
                m_NextPulseTime = Time.time + pulseInterval;
                PulseSupport();
            }
        }

        private void PulseSupport()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, radius);
            foreach (var col in hits)
            {
                ConquestPlayer player = col.GetComponentInParent<ConquestPlayer>();
                if (player != null && !player.Health.IsDead && player.CurrentTeam == stationTeam.Value)
                {
                    // Heal
                    if (player.Health.CurrentHealth < player.Health.MaxHealth)
                    {
                        int newHealth = Mathf.Min(player.Health.MaxHealth, player.Health.CurrentHealth + healAmount);
                        player.Health.currentHealth.Value = newHealth;
                    }

                    // Resupply ammo
                    ConquestWeapon wep = player.GetComponent<ConquestWeapon>();
                    if (wep != null)
                    {
                        wep.ResupplyAmmo();
                    }
                }
            }
        }

        public void TakeDamage(int damage)
        {
            if (!IsServer) return;
            currentHealth.Value = Mathf.Max(0, currentHealth.Value - damage);
            if (currentHealth.Value <= 0)
            {
                GetComponent<NetworkObject>()?.Despawn(true);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = stationTeam.Value == Team.Red ? Color.red : Color.blue;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
