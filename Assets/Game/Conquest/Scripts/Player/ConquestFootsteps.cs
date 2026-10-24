using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Audio;

namespace Game.Conquest.Player
{
    /// <summary>
    /// Footsteps and landings from how the character actually moves, on every peer. The animation
    /// events only existed on the forward run clip, so strafing and backpedalling were silent and a
    /// remote landing played at take-off. Uses the clips and source of whichever ConquestAnimationEvents
    /// is visible: the first-person rig on your own screen, the third-person one for everyone else.
    /// </summary>
    public class ConquestFootsteps : NetworkBehaviour
    {
        [Tooltip("Placeholder: metres walked between two footsteps.")]
        [SerializeField] private float strideLength = 2.2f;
        [Tooltip("Placeholder: slower than this (m/s, across the ground) makes no footsteps.")]
        [SerializeField] private float minSpeed = 1.2f;
        [Tooltip("Placeholder: falling faster than this (m/s) and then stopping plays a landing.")]
        [SerializeField] private float landSpeed = 4f;
        [SerializeField] private float footstepVolume = 0.16f;
        [SerializeField] private float landVolume = 0.35f;

        private ConquestAnimationEvents[] m_AllEvents;
        private ConquestAnimationEvents m_Events;
        private ConquestPlayer m_Player;
        private Vector3 m_LastPosition;
        private float m_Distance;
        private float m_FallSpeed;

        private void Awake()
        {
            m_AllEvents = GetComponentsInChildren<ConquestAnimationEvents>(true);
            m_Player = GetComponent<ConquestPlayer>();
        }

        public override void OnNetworkSpawn()
        {
            m_LastPosition = transform.position;
            // Only your own first-person rig is ever visible as 2D; a third-person body is always someone else.
            bool localView = IsOwner && m_Player != null && !m_Player.isBot;
            Transform cameraPivot = CameraPivot();
            for (int i = 0; i < m_AllEvents.Length; i++)
            {
                bool firstPerson = cameraPivot != null && m_AllEvents[i].transform.IsChildOf(cameraPivot);
                ConquestAudioSetup.ConfigureCharacterSource(m_AllEvents[i].AudioSource, localView && firstPerson, 96);
            }
        }

        private void Update()
        {
            if (!IsSpawned || Time.deltaTime <= 0f) return;
            // A dedicated server has nobody listening.
            if (!IsClient) return;
            if (m_Events == null || !m_Events.isActiveAndEnabled) m_Events = VisibleEvents();
            if (m_Events == null) return;

            Vector3 position = transform.position;
            Vector3 delta = position - m_LastPosition;
            m_LastPosition = position;

            // A teleport (respawn) isn't a step.
            if (delta.sqrMagnitude > 25f)
            {
                m_Distance = 0f;
                m_FallSpeed = 0f;
                return;
            }
            bool alive = m_Player == null || m_Player.Health == null || !m_Player.Health.IsDead;
            if (!alive) return;

            float verticalSpeed = delta.y / Time.deltaTime;
            float groundSpeed = new Vector2(delta.x, delta.z).magnitude / Time.deltaTime;
            bool airborne = Mathf.Abs(verticalSpeed) > 1.5f;

            if (verticalSpeed < -landSpeed) m_FallSpeed = Mathf.Max(m_FallSpeed, -verticalSpeed);
            else if (!airborne && m_FallSpeed > 0f)
            {
                m_Events.PlayLand(landVolume);
                m_FallSpeed = 0f;
                m_Distance = 0f;
            }

            if (airborne || groundSpeed < minSpeed)
            {
                if (groundSpeed < minSpeed) m_Distance = strideLength * 0.5f; // the first step comes quickly
                return;
            }
            m_Distance += groundSpeed * Time.deltaTime;
            if (m_Distance >= strideLength)
            {
                m_Distance -= strideLength;
                m_Events.PlayFootstep(footstepVolume);
            }
        }

        private ConquestAnimationEvents VisibleEvents()
        {
            for (int i = 0; i < m_AllEvents.Length; i++)
            {
                if (m_AllEvents[i] != null && m_AllEvents[i].isActiveAndEnabled) return m_AllEvents[i];
            }
            return null;
        }

        // The first-person rig and the camera share this parent.
        private Transform CameraPivot()
        {
            var movement = GetComponent<ConquestPlayerMovement>();
            return movement != null && movement.PlayerCamera != null ? movement.PlayerCamera.transform.parent : null;
        }
    }
}
