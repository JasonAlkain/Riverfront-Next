using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Classes;
using Game.Conquest.Controls;
using Game.Conquest.Gameplay;
using Game.Conquest.Player;
using Game.Conquest.UI;

namespace Game.Conquest.Combat
{
    /// <summary>
    /// A player's class throwable (grenades), thrown with the throw key (the same key that deploys
    /// an Engineer's station). The owner only asks; the server checks the count, the cooldown and
    /// where the throw starts, then spawns the grenade. A class with a support station has none.
    /// </summary>
    [RequireComponent(typeof(ConquestPlayer))]
    public class ConquestThrowables : NetworkBehaviour
    {
        [Tooltip("Max distance between the client's throw origin and the server's view of the player's camera.")]
        [SerializeField] private float maxOriginOffset = 3f;
        [Tooltip("How far in front of the camera the grenade appears, so it clears the thrower's own body.")]
        [SerializeField] private float spawnForward = 0.7f;
        [Tooltip("Placeholder: extra upward speed in m/s, so a level throw arcs.")]
        [SerializeField] private float upwardSpeed = 3f;

        /// <summary>Throwables left. Only the thrower's HUD shows it.</summary>
        public NetworkVariable<byte> count = new NetworkVariable<byte>(
            0,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        private ConquestPlayer m_Player;
        private ConquestHealth m_Health;
        private ConquestPlayerMovement m_Movement;
        private float m_NextThrowTime;
        private float m_ServerNextThrowTime;

        private ConquestClassData Data => m_Player != null ? m_Player.CurrentClassData : null;

        /// <summary>This class throws something (it has a throwable and a prefab for it).</summary>
        public bool HasThrowable
        {
            get
            {
                ConquestClassData data = Data;
                return data != null && data.throwable != ThrowableKind.None && data.throwablePrefab != null;
            }
        }

        public int Count => count.Value;
        public string ThrowableName => Data != null ? Data.throwableName : "";
        /// <summary>Owner: seconds until the next throw is allowed.</summary>
        public float CooldownRemaining => Mathf.Max(0f, m_NextThrowTime - Time.time);

        private void Awake()
        {
            m_Player = GetComponent<ConquestPlayer>();
            m_Health = GetComponent<ConquestHealth>();
            m_Movement = GetComponent<ConquestPlayerMovement>();
        }

        /// <summary>Server: a fresh spawn or a class change carries the class's full count.</summary>
        public void ServerRefill(ConquestClassData data)
        {
            if (!IsServer) return;
            int full = data != null && data.throwable != ThrowableKind.None ? data.throwableCount : 0;
            count.Value = (byte)Mathf.Clamp(full, 0, 255);
        }

        /// <summary>Server: a supply pulse gives one back, up to the class's count.</summary>
        public void ServerGiveOne()
        {
            if (!IsServer || !HasThrowable) return;
            if (count.Value < Data.throwableCount) count.Value++;
        }

        private void Update()
        {
            if (!IsOwner || m_Player == null || m_Player.isBot) return;
            if (!ConquestInput.DeployPressed || !HasThrowable || count.Value == 0) return;
            if (Time.time < m_NextThrowTime || Cursor.lockState != CursorLockMode.Locked) return;
            if (m_Health != null && m_Health.IsDead) return;
            if (ConquestMatchManager.Instance != null && ConquestMatchManager.Instance.IsMatchOver) return;
            if (ConquestPauseMenu.Instance != null && ConquestPauseMenu.Instance.IsPaused) return;

            Camera cam = m_Movement != null ? m_Movement.PlayerCamera : null;
            if (cam == null) return;
            m_NextThrowTime = Time.time + Data.throwCooldown;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            ThrowServerRpc(ray.origin, ray.direction);
        }

        [ServerRpc]
        private void ThrowServerRpc(Vector3 origin, Vector3 direction)
        {
            ServerThrow(origin, direction);
        }

        private void ServerThrow(Vector3 origin, Vector3 direction)
        {
            ConquestClassData data = Data;
            if (!HasThrowable || count.Value == 0) return;
            if (m_Health != null && m_Health.IsDead) return;
            if (ConquestMatchManager.Instance != null && ConquestMatchManager.Instance.IsMatchOver) return;
            // A little leeway for packets that arrive bunched up.
            if (Time.time < m_ServerNextThrowTime - 0.1f) return;
            if (!IsFinite(origin) || !IsFinite(direction) || direction.sqrMagnitude < 1e-6f) return;
            direction.Normalize();

            // Never trust where the client says the throw starts: near the server's camera, and
            // never from the far side of a wall.
            Vector3 expected = m_Movement != null && m_Movement.PlayerCamera != null
                ? m_Movement.PlayerCamera.transform.position
                : transform.position + Vector3.up * 1.6f;
            if ((origin - expected).sqrMagnitude > maxOriginOffset * maxOriginOffset) return;
            Vector3 spawn = origin + direction * spawnForward;
            if (ConquestPhysics.LinecastIgnoring(transform, expected, spawn, ConquestPhysics.WorldMask, out _)) spawn = expected;

            count.Value--;
            m_ServerNextThrowTime = Time.time + data.throwCooldown;
            if (m_Health != null) m_Health.EndSpawnProtection();

            GameObject thrown = Instantiate(data.throwablePrefab, spawn, Quaternion.LookRotation(direction));
            var grenade = thrown.GetComponent<ConquestGrenade>();
            var netObj = thrown.GetComponent<NetworkObject>();
            if (grenade == null || netObj == null)
            {
                Debug.LogError($"[ConquestThrowables] {data.throwablePrefab.name} needs a NetworkObject and a ConquestGrenade.", this);
                Destroy(thrown);
                return;
            }
            Vector3 velocity = direction * data.throwSpeed + Vector3.up * upwardSpeed;
            grenade.ServerArm(this, m_Player.CurrentTeam, data.throwableName, velocity);
            netObj.Spawn(true);
        }

        private static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                     float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }
    }
}
