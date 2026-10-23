using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Controls;
using Game.Conquest.Combat;

namespace Game.Conquest.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class ConquestPlayerMovement : NetworkBehaviour
    {
        [Header("Fall Limit")]
        [SerializeField] private float fallLimitY = -25.0f;
        public float FallLimitY => fallLimitY;

        [Header("Movement Settings")]
        [SerializeField] private float walkSpeed = 6.0f;
        [SerializeField] private float sprintSpeed = 10.0f;
        [SerializeField] private float jumpHeight = 1.8f;
        [SerializeField] private float gravity = -20.0f;
        public bool isBot = false;

        public void SetSpeed(float walk, float sprint)
        {
            walkSpeed = walk;
            sprintSpeed = sprint;
        }

        [Header("Camera & Look Settings")]
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private float mouseSensitivity = 0.15f;
        private float m_ZoomedSensitivity = 1f;
        private bool m_InvertY;
        [SerializeField] private float minPitch = -85.0f;
        [SerializeField] private float maxPitch = 85.0f;

        [Header("References")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private AudioListener audioListener;

        [Header("Viewmodel Alignment")]
        [SerializeField] private Vector3 armsLocalOffset = new Vector3(0.035f, -1.35f, 0.05f);

        // Synchronize vertical pitch across network for third-person aim animations
        public NetworkVariable<float> networkPitch = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private CharacterController m_CharacterController;
        private Unity.Netcode.Components.NetworkTransform m_NetworkTransform;
        private ConquestHealth m_Health;
        private Vector3 m_Velocity;
        private float m_Pitch;
        private bool m_CursorLocked = true;
        private bool m_LocalViewActive = false;
        private float m_BaseFieldOfView = 75.0f;

        // Solid colliders on this body and whether each started enabled (the bot's
        // CharacterController is disabled in its prefab and must stay that way).
        private Collider[] m_BodyColliders;
        private bool[] m_BodyColliderDefaults;
        private bool m_BodyCollisionEnabled = true;

        public Camera PlayerCamera => playerCamera;
        public Transform CameraPivot => cameraPivot;
        public float Pitch => m_Pitch;
        /// <summary>The unzoomed field of view from the player's settings.</summary>
        public float BaseFieldOfView => m_BaseFieldOfView;

        private void Awake()
        {
            m_CharacterController = GetComponent<CharacterController>();
            m_NetworkTransform = GetComponent<Unity.Netcode.Components.NetworkTransform>();
            m_Health = GetComponent<ConquestHealth>();
            m_Weapon = GetComponent<ConquestWeapon>();

            var colliders = new System.Collections.Generic.List<Collider>();
            foreach (var c in GetComponentsInChildren<Collider>(true))
            {
                if (!c.isTrigger) colliders.Add(c);
            }
            m_BodyColliders = colliders.ToArray();
            m_BodyColliderDefaults = new bool[m_BodyColliders.Length];
            for (int i = 0; i < m_BodyColliders.Length; i++) m_BodyColliderDefaults[i] = m_BodyColliders[i].enabled;
        }

        /// <summary>
        /// Turns this body's solid colliders off while dead, so an invisible corpse can't absorb
        /// shots or block movement, and back on (to their prefab state) on respawn.
        /// </summary>
        public void SetBodyCollisionEnabled(bool enabled)
        {
            m_BodyCollisionEnabled = enabled;
            for (int i = 0; i < m_BodyColliders.Length; i++)
            {
                if (m_BodyColliders[i] != null) m_BodyColliders[i].enabled = enabled && m_BodyColliderDefaults[i];
            }
        }

        public override void OnNetworkSpawn()
        {
            if (playerCamera != null)
            {
                playerCamera.nearClipPlane = 0.05f;
            }

            if (IsOwner && !isBot)
            {
                // The player object is spawned as soon as host/client starts, which is still in the
                // MainMenu lobby. Only take over camera, AudioListener and cursor once a match is running;
                // Update() activates the view when the match manager appears.
                SetLocalViewActive(IsMatchActive());

                ApplySettings(Game.Conquest.Settings.ConquestSettingsManager.Instance?.CurrentSettings);
                if (Game.Conquest.Settings.ConquestSettingsManager.Instance != null)
                {
                    Game.Conquest.Settings.ConquestSettingsManager.Instance.OnSettingsChanged += ApplySettings;
                }
            }
            else
            {
                if (playerCamera != null)
                {
                    playerCamera.enabled = false;
                }
                if (audioListener != null)
                {
                    audioListener.enabled = false;
                }
            }

            ApplyArmsOffset();
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner && !isBot && Game.Conquest.Settings.ConquestSettingsManager.Instance != null)
            {
                Game.Conquest.Settings.ConquestSettingsManager.Instance.OnSettingsChanged -= ApplySettings;
            }
        }

        private void ApplySettings(Game.Conquest.Settings.ConquestSettingsData settings)
        {
            if (settings == null) return;
            mouseSensitivity = settings.mouseSensitivity;
            m_ZoomedSensitivity = settings.zoomSensitivity;
            m_InvertY = settings.invertY;
            m_BaseFieldOfView = settings.fieldOfView;
            if (playerCamera != null)
            {
                playerCamera.fieldOfView = settings.fieldOfView;
            }
        }

        public void ApplyArmsOffset()
        {
            if (cameraPivot != null)
            {
                var arm1p = cameraPivot.Find("FirstPersonVisuals/Armature_1P");
                if (arm1p != null)
                {
                    arm1p.localPosition = armsLocalOffset;
                }
            }
        }

        private void Update()
        {
            if (isBot) return;
            if (!IsOwner) return;

            if (!m_LocalViewActive && IsMatchActive())
            {
                SetLocalViewActive(true);
            }

            // Do not move or apply gravity before match is active or during scene transition
            if (Game.Conquest.Gameplay.ConquestMatchManager.Instance == null || 
                (Game.Conquest.Scenes.ConquestSceneTransitionManager.Instance != null && Game.Conquest.Scenes.ConquestSceneTransitionManager.Instance.IsTransitioning))
            {
                m_Velocity = Vector3.zero;
                return;
            }

            // The match is decided: everyone stands still under the victory/defeat banner.
            if (Game.Conquest.Gameplay.ConquestMatchManager.Instance.IsMatchOver)
            {
                m_Velocity = Vector3.zero;
                return;
            }

            if (Game.Conquest.UI.ConquestPauseMenu.Instance != null && Game.Conquest.UI.ConquestPauseMenu.Instance.IsPaused)
            {
                return;
            }

            // Fall limit check
            if (transform.position.y < fallLimitY)
            {
                TriggerFallDeath();
                return;
            }

            if (m_Health != null && m_Health.IsDead)
            {
                if (m_CursorLocked)
                {
                    SetCursorLock(false);
                }
                UpdateDeathCamera();
                return;
            }
            EndDeathCamera();

            HandleCursorToggle();
            if (!m_CursorLocked) return;

            HandleLook();
            HandleMovement();
        }

        /// <summary>
        /// Server: this combatant's horizontal velocity as the server sees it (smoothed). It is
        /// exactly what server-side bolts are tested against, so bots lead with it directly.
        /// </summary>
        public Vector3 ServerVelocity { get; private set; }
        private Vector3 m_ServerLastPos;
        private bool m_ServerHadPos;

        // Separate from Update, which returns early for bots and for players this peer doesn't own.
        private void LateUpdate()
        {
            if (!IsServer || !IsSpawned) return;

            Vector3 pos = transform.position;
            Vector3 delta = pos - m_ServerLastPos;
            delta.y = 0f;
            float dt = Time.deltaTime;
            var settings = BoltSettings;
            bool dead = m_Health != null && m_Health.IsDead;
            if (!m_ServerHadPos || dead || dt <= 0f || delta.magnitude > settings.teleportResetDistance)
            {
                // A respawn or teleport (which remote players do on their own peer) isn't movement.
                ServerVelocity = Vector3.zero;
            }
            else
            {
                Vector3 v = delta / dt;
                float blend = settings.velocitySmoothingSeconds > 0f ? 1f - Mathf.Exp(-dt / settings.velocitySmoothingSeconds) : 1f;
                ServerVelocity = Vector3.ClampMagnitude(Vector3.Lerp(ServerVelocity, v, blend), sprintSpeed * settings.velocityClampFactor);
            }
            m_ServerLastPos = pos;
            m_ServerHadPos = !dead;
        }

        private ConquestPlayer m_OwnerPlayer;

        private Game.Conquest.Classes.ConquestBoltSettings BoltSettings
        {
            get
            {
                if (m_OwnerPlayer == null) m_OwnerPlayer = GetComponent<ConquestPlayer>();
                var roster = m_OwnerPlayer != null ? m_OwnerPlayer.ClassRoster : null;
                return roster != null ? roster.BoltSettings : Game.Conquest.Classes.ConquestBoltSettings.Fallback;
            }
        }

        private void TriggerFallDeath()
        {
            if (m_Health != null && !m_Health.IsDead)
            {
                if (IsServer)
                {
                    m_Health.TakeDamage(m_Health.MaxHealth * 10, Game.Conquest.Combat.ConquestHealth.NoAttacker);
                }
                else
                {
                    RequestFallDeathServerRpc();
                }
            }
        }

        [ServerRpc]
        private void RequestFallDeathServerRpc()
        {
            // Only honour the request if the server also sees the player below the kill height
            // (small margin for owner-authoritative transform lag).
            if (transform.position.y > fallLimitY + 2f) return;
            if (m_Health != null && !m_Health.IsDead)
            {
                m_Health.TakeDamage(m_Health.MaxHealth * 10, Game.Conquest.Combat.ConquestHealth.NoAttacker);
            }
        }

        private void HandleCursorToggle()
        {
            if (Game.Conquest.UI.ConquestPauseMenu.Instance != null && Game.Conquest.UI.ConquestPauseMenu.Instance.IsPaused)
            {
                return;
            }

            if (ConquestInput.FirePressed && !m_CursorLocked)
            {
                SetCursorLock(true);
            }
        }

        private static bool IsMatchActive()
        {
            return Game.Conquest.Gameplay.ConquestMatchManager.Instance != null;
        }

        private void SetLocalViewActive(bool active)
        {
            m_LocalViewActive = active;
            if (playerCamera != null) playerCamera.enabled = active;
            if (audioListener != null) audioListener.enabled = active;
            SetCursorLock(active);
        }

        public void SetCursorLock(bool locked)
        {
            // Never lock the cursor over menu UI (e.g. the lobby, where the player object already exists).
            if (locked && !IsMatchActive()) locked = false;
            m_CursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void HandleLook()
        {
            Vector2 mouseDelta = ConquestInput.LookDegrees(mouseSensitivity) * ZoomSensitivityScale();

            // Horizontal rotation on player root (networked via ClientNetworkTransform)
            transform.Rotate(Vector3.up * mouseDelta.x);

            // Vertical rotation on camera pivot
            if (cameraPivot != null)
            {
                m_Pitch += (m_InvertY ? mouseDelta.y : -mouseDelta.y);
                m_Pitch = Mathf.Clamp(m_Pitch, minPitch, maxPitch);
                cameraPivot.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);

                if (IsOwner && IsSpawned)
                {
                    networkPitch.Value = m_Pitch;
                }
            }
        }

        // While zoomed (smaller FOV) the view turns proportionally slower, so the same mouse
        // movement sweeps the same distance across the screen at any zoom.
        [Header("Death Camera")]
        [Tooltip("Placeholder: metres the view rises and pulls back after dying.")]
        [SerializeField] private Vector2 deathCameraOffset = new Vector2(1.4f, 2.6f);
        [Tooltip("Placeholder: seconds to move out to the death view.")]
        [SerializeField] private float deathCameraSeconds = 0.5f;
        private bool m_DeathCamActive;
        private float m_DeathCamStart;
        private Vector3 m_CamRestPosition;
        private Quaternion m_CamRestRotation;
        private Vector3 m_DeathCamFrom;

        [Header("Aim Down Sights")]
        [Tooltip("Placeholder: walk speed while fully aimed, as a share of the walk speed. Aiming also stops sprinting.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float aimMoveScale = 0.75f;
        private ConquestWeapon m_Weapon;

        // Owner, dead: the view rises and pulls back from where you fell and turns to whoever
        // killed you (or down at the spot), until you deploy again.
        private void UpdateDeathCamera()
        {
            if (playerCamera == null || isBot) return;
            Transform cam = playerCamera.transform;
            if (!m_DeathCamActive)
            {
                m_DeathCamActive = true;
                m_DeathCamStart = Time.time;
                m_CamRestPosition = cam.localPosition;
                m_CamRestRotation = cam.localRotation;
                m_DeathCamFrom = cam.position;
            }

            Vector3 focus = m_DeathCamFrom - Vector3.up * 1.2f;
            if (m_Health.LastAttackerId != ConquestHealth.NoAttacker && NetworkManager != null &&
                NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(m_Health.LastAttackerId, out var killer) && killer != null)
            {
                focus = killer.transform.position + Vector3.up * 1.4f;
            }
            Vector3 away = m_DeathCamFrom - focus;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : -transform.forward;
            Vector3 target = m_DeathCamFrom + Vector3.up * deathCameraOffset.x + away * deathCameraOffset.y;
            float t = deathCameraSeconds > 0f ? Mathf.SmoothStep(0f, 1f, (Time.time - m_DeathCamStart) / deathCameraSeconds) : 1f;
            cam.position = Vector3.Lerp(m_DeathCamFrom, target, t);
            Vector3 look = focus - cam.position;
            if (look.sqrMagnitude > 0.01f) cam.rotation = Quaternion.Slerp(cam.rotation, Quaternion.LookRotation(look), t);
        }

        private void EndDeathCamera()
        {
            if (!m_DeathCamActive || playerCamera == null) return;
            m_DeathCamActive = false;
            playerCamera.transform.localPosition = m_CamRestPosition;
            playerCamera.transform.localRotation = m_CamRestRotation;
        }

        private float ZoomSensitivityScale()
        {
            if (playerCamera == null || m_BaseFieldOfView <= 1f) return 1f;
            float baseHalf = Mathf.Tan(m_BaseFieldOfView * 0.5f * Mathf.Deg2Rad);
            float currentHalf = Mathf.Tan(playerCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float scale = Mathf.Clamp(currentHalf / baseHalf, 0.05f, 1f);
            // The player's zoomed-sensitivity setting only applies while actually zoomed in.
            return scale < 0.99f ? scale * m_ZoomedSensitivity : scale;
        }

        private void HandleMovement()
        {
            // Clamped rather than normalised so a half-tilted stick walks slower.
            Vector2 moveInput = Vector2.ClampMagnitude(ConquestInput.Move, 1f);
            float aim = m_Weapon != null ? m_Weapon.Aim01 : 0f;
            bool isSprinting = ConquestInput.SprintHeld && aim <= 0f;
            bool jumpPressed = ConquestInput.JumpPressed;

            float currentSpeed = isSprinting ? sprintSpeed : walkSpeed * Mathf.Lerp(1f, aimMoveScale, aim);
            Vector3 moveDirection = transform.right * moveInput.x + transform.forward * moveInput.y;

            bool isGrounded = m_CharacterController.isGrounded;
            if (isGrounded && m_Velocity.y < 0f)
            {
                m_Velocity.y = -2.0f;
            }

            if (jumpPressed && isGrounded)
            {
                m_Velocity.y = Mathf.Sqrt(jumpHeight * -2.0f * gravity);
            }

            m_Velocity.y += gravity * Time.deltaTime;

            Vector3 finalMovement = (moveDirection * currentSpeed + Vector3.up * m_Velocity.y) * Time.deltaTime;
            m_CharacterController.Move(finalMovement);
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            m_Pitch = 0f;
            if (cameraPivot != null)
            {
                cameraPivot.localRotation = Quaternion.identity;
            }

            if (isBot)
            {
                var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
                {
                    agent.Warp(position);
                }
                else
                {
                    transform.position = position;
                }
                transform.rotation = rotation;
                CommitTeleport(position, rotation);
                return;
            }

            if (m_CharacterController != null) m_CharacterController.enabled = false;
            transform.position = position;
            transform.rotation = rotation;
            m_Velocity = Vector3.zero;
            CommitTeleport(position, rotation);
            // A dead player parked for deploy stays without collision.
            if (m_CharacterController != null) m_CharacterController.enabled = m_BodyCollisionEnabled;
        }

        /// <summary>True on the peer allowed to move this object (owner for players, server for bots).</summary>
        public bool HasTransformAuthority => m_NetworkTransform == null || m_NetworkTransform.CanCommitToTransform;

        // Flag the move as a teleport so observers snap to the spawn point instead of
        // interpolating a slide across the map.
        private void CommitTeleport(Vector3 position, Quaternion rotation)
        {
            if (m_NetworkTransform != null && m_NetworkTransform.IsSpawned && m_NetworkTransform.CanCommitToTransform)
            {
                m_NetworkTransform.Teleport(position, rotation, transform.localScale);
            }
        }
    }
}
