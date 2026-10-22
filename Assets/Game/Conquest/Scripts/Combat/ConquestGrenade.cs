using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Gameplay;
using Game.Conquest.Player;

namespace Game.Conquest.Combat
{
    /// <summary>
    /// A thrown grenade. The server simulates the physics (clients follow its NetworkTransform),
    /// and when the fuse runs out it damages enemies, the thrower, and enemy stations and droids
    /// in range that the blast can see, with damage falling off toward the edge. Teammates are
    /// safe, the same as with bolts.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ConquestGrenade : NetworkBehaviour
    {
        [Header("Blast (placeholders)")]
        [Tooltip("Placeholder: seconds from the throw to the blast.")]
        [SerializeField] private float fuseSeconds = 2.5f;
        [Tooltip("Placeholder: blast radius in metres.")]
        [SerializeField] private float radius = 6f;
        [Tooltip("Placeholder: damage at the centre of the blast.")]
        [SerializeField] private int maxDamage = 120;
        [Tooltip("Placeholder: damage at the edge of the blast.")]
        [SerializeField] private int minDamage = 20;

        [Header("Visuals & Audio")]
        [Tooltip("Played at the blast on every peer.")]
        [SerializeField] private GameObject explosionPrefab;
        [SerializeField] private float explosionSeconds = 1.2f;
        [SerializeField] private AudioClip explosionClip;
        [SerializeField] private float explosionVolume = 1f;
        [Tooltip("Hidden at the blast, while the despawn reaches everyone.")]
        [SerializeField] private Renderer bodyRenderer;

        // The blast's clients need a moment to receive the explosion RPC before the object goes.
        private const float DespawnDelay = 0.25f;
        // Only world geometry hides you from a blast; players and deployables don't.
        private const int BlastCoverMask = 1 << 0;

        private static readonly Collider[] s_Overlap = new Collider[64];
        private static readonly List<Object> s_Damaged = new List<Object>();

        private ConquestThrowables m_Thrower;
        private ulong m_ThrowerId = ConquestHealth.NoAttacker;
        private Team m_Team;
        private string m_SourceName;
        private Vector3 m_LaunchVelocity;
        private bool m_Launched;
        private float m_ExplodeAt;
        private float m_DespawnAt;
        private bool m_Exploded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Damaged.Clear();
            System.Array.Clear(s_Overlap, 0, s_Overlap.Length);
        }

        /// <summary>Server, before Spawn: who threw it, for which team, and how hard.</summary>
        public void ServerArm(ConquestThrowables thrower, Team team, string sourceName, Vector3 velocity)
        {
            m_Thrower = thrower;
            m_ThrowerId = thrower != null ? thrower.NetworkObjectId : ConquestHealth.NoAttacker;
            m_Team = team;
            m_SourceName = sourceName;
            m_LaunchVelocity = velocity;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            m_ExplodeAt = Time.time + fuseSeconds;

            // It leaves from inside the thrower's reach: don't let it bounce off their own body.
            if (m_Thrower != null)
            {
                Collider own = GetComponent<Collider>();
                foreach (Collider c in m_Thrower.GetComponentsInChildren<Collider>())
                {
                    if (own != null) Physics.IgnoreCollision(own, c);
                }
            }
        }

        // The launch waits for the first physics step, once NetworkRigidbody has set the body up.
        private void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || m_Launched) return;
            m_Launched = true;
            var body = GetComponent<Rigidbody>();
            body.isKinematic = false;
            body.linearVelocity = m_LaunchVelocity;
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned) return;
            if (!m_Exploded)
            {
                if (Time.time >= m_ExplodeAt) Explode();
                return;
            }
            if (Time.time >= m_DespawnAt) NetworkObject.Despawn(true);
        }

        private void Explode()
        {
            m_Exploded = true;
            m_DespawnAt = Time.time + DespawnDelay;
            GetComponent<Rigidbody>().isKinematic = true;
            Vector3 center = transform.position;
            ExplodeClientRpc(center);

            bool matchOver = ConquestMatchManager.Instance != null && ConquestMatchManager.Instance.IsMatchOver;
            if (!matchOver) DamageAround(center);
        }

        private void DamageAround(Vector3 center)
        {
            ConquestWeapon throwerWeapon = m_Thrower != null ? m_Thrower.GetComponent<ConquestWeapon>() : null;
            s_Damaged.Clear();
            int hits = Physics.OverlapSphereNonAlloc(center, radius, s_Overlap, ConquestPhysics.ShotMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
            {
                Collider col = s_Overlap[i];
                if (col == null) continue;
                Vector3 target = col.bounds.center;

                ConquestHealth health = col.GetComponentInParent<ConquestHealth>();
                if (health != null)
                {
                    if (s_Damaged.Contains(health)) continue;
                    s_Damaged.Add(health);
                    ConquestPlayer player = health.GetComponent<ConquestPlayer>();
                    if (player == null || health.IsDead) continue;
                    bool self = player.NetworkObjectId == m_ThrowerId;
                    if (!self && player.CurrentTeam == m_Team) continue;
                    if (IsCovered(center, target)) continue;
                    bool wasAlive = !health.IsDead;
                    health.TakeDamage(DamageAt(center, target), m_ThrowerId, center, m_SourceName);
                    if (!self && throwerWeapon != null) throwerWeapon.ServerNoteHit(wasAlive && health.IsDead);
                    continue;
                }

                ConquestSupportStation station = col.GetComponentInParent<ConquestSupportStation>();
                if (station != null)
                {
                    if (s_Damaged.Contains(station)) continue;
                    s_Damaged.Add(station);
                    if (station.stationTeam.Value != m_Team && !IsCovered(center, target)) station.TakeDamage(DamageAt(center, target));
                    continue;
                }

                ConquestSupplyDroid droid = col.GetComponentInParent<ConquestSupplyDroid>();
                if (droid != null)
                {
                    if (s_Damaged.Contains(droid)) continue;
                    s_Damaged.Add(droid);
                    if (!droid.IsBroken && droid.CanBeDamagedBy(m_Team) && !IsCovered(center, target)) droid.TakeDamage(DamageAt(center, target));
                }
            }
            s_Damaged.Clear();
            System.Array.Clear(s_Overlap, 0, hits);
        }

        private static bool IsCovered(Vector3 center, Vector3 target)
        {
            // Start a little above the grenade, so the floor it rests on doesn't count as cover.
            return Physics.Linecast(center + Vector3.up * 0.2f, target, BlastCoverMask, QueryTriggerInteraction.Ignore);
        }

        private int DamageAt(Vector3 center, Vector3 target)
        {
            float t = radius > 0f ? Mathf.Clamp01(Vector3.Distance(center, target) / radius) : 0f;
            return Mathf.RoundToInt(Mathf.Lerp(maxDamage, minDamage, t));
        }

        [ClientRpc]
        private void ExplodeClientRpc(Vector3 center)
        {
            if (bodyRenderer != null) bodyRenderer.enabled = false;
            if (explosionPrefab != null) ConquestFxPool.Play(explosionPrefab, center, Quaternion.identity, explosionSeconds);
            if (explosionClip != null) AudioSource.PlayClipAtPoint(explosionClip, center, explosionVolume);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0.1f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
