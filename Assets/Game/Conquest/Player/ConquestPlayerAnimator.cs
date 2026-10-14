using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Combat;

namespace Game.Conquest.Player
{
    public class ConquestPlayerAnimator : NetworkBehaviour
    {
        [Header("Animators")]
        [SerializeField] private Animator firstPersonAnimator;
        [SerializeField] private Animator thirdPersonAnimator;

        [Header("References")]
        [SerializeField] private ConquestPlayerMovement movement;
        [SerializeField] private ConquestHealth health;
        [SerializeField] private UnityEngine.CharacterController characterController;

        [Header("Locomotion Tuning")]
        [SerializeField] private float walkSpeed = 6.0f;
        [SerializeField] private float speedDampTime = 0.15f;

        // Animator parameter hashes
        private static readonly int s_Speed = Animator.StringToHash("Speed");
        private static readonly int s_SpeedStrafe = Animator.StringToHash("SpeedStrafe");
        private static readonly int s_IsMoving = Animator.StringToHash("IsMoving");
        private static readonly int s_Shoot = Animator.StringToHash("Shoot");
        private static readonly int s_IsShooting = Animator.StringToHash("IsShooting");
        private static readonly int s_Reload = Animator.StringToHash("Reload");
        private static readonly int s_Jump = Animator.StringToHash("Jump");
        private static readonly int s_Land = Animator.StringToHash("Land");
        private static readonly int s_IsInAir = Animator.StringToHash("IsInAir");
        private static readonly int s_IsHit = Animator.StringToHash("IsHit");
        private static readonly int s_IsDead = Animator.StringToHash("IsDead");
        private static readonly int s_AimPitch = Animator.StringToHash("AimPitch");

        private Vector3 m_LastPosition;
        private bool m_WasGrounded = true;
        private float m_CurrentSpeed;
        private float m_CurrentStrafe;
        private float m_CurrentIsMoving;

        private void Awake()
        {
            if (movement == null) movement = GetComponent<ConquestPlayerMovement>();
            if (health == null) health = GetComponent<ConquestHealth>();
            if (characterController == null) characterController = GetComponent<UnityEngine.CharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            if (health != null)
            {
                health.OnDied += HandleDied;
                health.OnRespawned += HandleRespawned;
            }
            m_LastPosition = transform.position;
        }

        public override void OnNetworkDespawn()
        {
            if (health != null)
            {
                health.OnDied -= HandleDied;
                health.OnRespawned -= HandleRespawned;
            }
        }

        private void Update()
        {
            UpdateLocomotion();
            UpdatePitch();
        }

        private void UpdateLocomotion()
        {
            float dt = Mathf.Max(0.0001f, Time.deltaTime);

            // Calculate movement velocity vector in local player root space
            Vector3 worldVelocity = (transform.position - m_LastPosition) / dt;
            m_LastPosition = transform.position;

            Vector3 localVelocity = transform.InverseTransformDirection(worldVelocity);

            // Normalize speeds against max walk/sprint speed to match BlendTree thresholds (-1 to 1)
            float maxSpeed = Mathf.Max(1.0f, walkSpeed);
            float targetForward = Mathf.Clamp(localVelocity.z / maxSpeed, -1.0f, 1.0f);
            float targetStrafe = Mathf.Clamp(localVelocity.x / maxSpeed, -1.0f, 1.0f);
            float targetPlanar = Mathf.Clamp01(new Vector2(localVelocity.x, localVelocity.z).magnitude / maxSpeed);

            // Smooth values to prevent over-sensitive jerkiness
            m_CurrentSpeed = Mathf.Lerp(m_CurrentSpeed, targetForward, dt / speedDampTime);
            m_CurrentStrafe = Mathf.Lerp(m_CurrentStrafe, targetStrafe, dt / speedDampTime);
            m_CurrentIsMoving = Mathf.Lerp(m_CurrentIsMoving, targetPlanar, dt / speedDampTime);

            bool isGrounded = characterController != null && characterController.enabled ? characterController.isGrounded : true;

            // Update 1st Person Animator (if active)
            if (firstPersonAnimator != null && firstPersonAnimator.gameObject.activeInHierarchy)
            {
                firstPersonAnimator.SetFloat(s_IsMoving, m_CurrentIsMoving);
                firstPersonAnimator.SetBool(s_IsInAir, !isGrounded);
            }

            // Update 3rd Person Animator (if active)
            if (thirdPersonAnimator != null && thirdPersonAnimator.gameObject.activeInHierarchy)
            {
                thirdPersonAnimator.SetFloat(s_Speed, m_CurrentSpeed);
                thirdPersonAnimator.SetFloat(s_SpeedStrafe, m_CurrentStrafe);
                thirdPersonAnimator.SetBool(s_IsInAir, !isGrounded);
            }

            // Jump trigger
            if (m_WasGrounded && !isGrounded && localVelocity.y > 0.5f)
            {
                if (firstPersonAnimator != null && firstPersonAnimator.gameObject.activeInHierarchy)
                    firstPersonAnimator.SetTrigger(s_Jump);
                if (thirdPersonAnimator != null && thirdPersonAnimator.gameObject.activeInHierarchy)
                    thirdPersonAnimator.SetTrigger(s_Jump);
            }

            // Land trigger
            if (!m_WasGrounded && isGrounded)
            {
                if (firstPersonAnimator != null && firstPersonAnimator.gameObject.activeInHierarchy)
                    firstPersonAnimator.SetTrigger(s_Land);
                if (thirdPersonAnimator != null && thirdPersonAnimator.gameObject.activeInHierarchy)
                    thirdPersonAnimator.SetTrigger(s_Land);
            }

            m_WasGrounded = isGrounded;
        }

        private void UpdatePitch()
        {
            if (thirdPersonAnimator != null && thirdPersonAnimator.gameObject.activeInHierarchy && movement != null)
            {
                float pitchDeg = IsOwner 
                    ? movement.Pitch
                    : movement.networkPitch.Value;

                // Normalize pitch between -1.0 (looking 85 deg up) and 1.0 (looking 85 deg down)
                // matching the 3P Aim BlendTree thresholds [-1 = Up 90, 0 = Aim, 1 = Down 90]
                float normalizedPitch = Mathf.Clamp(pitchDeg / 85.0f, -1.0f, 1.0f);
                thirdPersonAnimator.SetFloat(s_AimPitch, normalizedPitch);
            }
        }

        public void TriggerFire()
        {
            if (firstPersonAnimator != null && firstPersonAnimator.gameObject.activeInHierarchy)
            {
                firstPersonAnimator.SetTrigger(s_Shoot);
            }
            if (thirdPersonAnimator != null && thirdPersonAnimator.gameObject.activeInHierarchy)
            {
                thirdPersonAnimator.SetTrigger(s_Shoot);
            }
        }

        public void TriggerReload()
        {
            if (firstPersonAnimator != null && firstPersonAnimator.gameObject.activeInHierarchy)
            {
                firstPersonAnimator.SetTrigger(s_Reload);
            }
            if (thirdPersonAnimator != null && thirdPersonAnimator.gameObject.activeInHierarchy)
            {
                thirdPersonAnimator.SetTrigger(s_Reload);
            }
        }

        public void TriggerHit()
        {
            if (thirdPersonAnimator != null && thirdPersonAnimator.gameObject.activeInHierarchy)
            {
                thirdPersonAnimator.SetTrigger(s_IsHit);
            }
        }

        private void HandleDied(ulong killerId)
        {
            if (thirdPersonAnimator != null && thirdPersonAnimator.gameObject.activeInHierarchy)
            {
                thirdPersonAnimator.SetBool(s_IsDead, true);
            }
        }

        private void HandleRespawned()
        {
            if (thirdPersonAnimator != null && thirdPersonAnimator.gameObject.activeInHierarchy)
            {
                thirdPersonAnimator.SetBool(s_IsDead, false);
            }
            if (firstPersonAnimator != null && firstPersonAnimator.gameObject.activeInHierarchy)
            {
                firstPersonAnimator.SetBool(s_IsDead, false);
            }
        }

        public void SetAnimators(Animator fpAnim, Animator tpAnim)
        {
            firstPersonAnimator = fpAnim;
            thirdPersonAnimator = tpAnim;
        }
    }
}
