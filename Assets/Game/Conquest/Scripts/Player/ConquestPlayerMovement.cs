using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
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
        private ConquestHealth m_Health;
        private Vector3 m_Velocity;
        private float m_Pitch;
        private bool m_CursorLocked = true;
        private bool m_LocalViewActive = false;

        public Camera PlayerCamera => playerCamera;
        public Transform CameraPivot => cameraPivot;
        public float Pitch => m_Pitch;

        private void Awake()
        {
            m_CharacterController = GetComponent<CharacterController>();
            m_Health = GetComponent<ConquestHealth>();
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
                return;
            }

            HandleCursorToggle();
            if (!m_CursorLocked) return;

            HandleLook();
            HandleMovement();
        }

        private void TriggerFallDeath()
        {
            if (m_Health != null && !m_Health.IsDead)
            {
                if (IsServer)
                {
                    m_Health.TakeDamage(m_Health.MaxHealth * 10, 0);
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
                m_Health.TakeDamage(m_Health.MaxHealth * 10, 0);
            }
        }

        private void HandleCursorToggle()
        {
            if (Game.Conquest.UI.ConquestPauseMenu.Instance != null && Game.Conquest.UI.ConquestPauseMenu.Instance.IsPaused)
            {
                return;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !m_CursorLocked)
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
            if (Mouse.current == null) return;

            Vector2 mouseDelta = Mouse.current.delta.ReadValue() * mouseSensitivity;

            // Horizontal rotation on player root (networked via ClientNetworkTransform)
            transform.Rotate(Vector3.up * mouseDelta.x);

            // Vertical rotation on camera pivot
            if (cameraPivot != null)
            {
                bool invert = Game.Conquest.Settings.ConquestSettingsManager.Instance != null && Game.Conquest.Settings.ConquestSettingsManager.Instance.CurrentSettings.invertY;
                m_Pitch += (invert ? mouseDelta.y : -mouseDelta.y);
                m_Pitch = Mathf.Clamp(m_Pitch, minPitch, maxPitch);
                cameraPivot.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);

                if (IsOwner && IsSpawned)
                {
                    networkPitch.Value = m_Pitch;
                }
            }
        }

        private void HandleMovement()
        {
            Vector2 moveInput = Vector2.zero;
            bool isSprinting = false;
            bool jumpPressed = false;

            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed) moveInput.y += 1f;
                if (Keyboard.current.sKey.isPressed) moveInput.y -= 1f;
                if (Keyboard.current.aKey.isPressed) moveInput.x -= 1f;
                if (Keyboard.current.dKey.isPressed) moveInput.x += 1f;

                isSprinting = Keyboard.current.leftShiftKey.isPressed;
                jumpPressed = Keyboard.current.spaceKey.wasPressedThisFrame;
            }

            moveInput.Normalize();

            float currentSpeed = isSprinting ? sprintSpeed : walkSpeed;
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
                return;
            }

            if (m_CharacterController != null) m_CharacterController.enabled = false;
            transform.position = position;
            transform.rotation = rotation;
            m_Velocity = Vector3.zero;
            if (m_CharacterController != null) m_CharacterController.enabled = true;
        }
    }
}
