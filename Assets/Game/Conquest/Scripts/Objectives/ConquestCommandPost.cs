using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using Game.Conquest.Gameplay;
using Game.Conquest.Player;

namespace Game.Conquest.Objectives
{
    public class ConquestCommandPost : NetworkBehaviour
    {
        [Header("Post Identity")]
        [SerializeField] private string postName = "Alpha";
        [SerializeField] private Team initialOwner = Team.Neutral;

        [Header("Capture Rules")]
        [SerializeField] private float captureRadius = 8.0f;
        [Tooltip("How far above or below the post a player still counts, so a roof or a floor below doesn't capture.")]
        [SerializeField] private float captureHeight = 4.0f;
        [SerializeField] private float captureTime = 10.0f; // Seconds to capture from 0 to 100%
        [Tooltip("Progress per second an empty post drifts back toward its owner (or toward neutral). 0 holds partial progress.")]
        [SerializeField] private float idleRecoveryRate = 0f;

        [Header("Spawn Locations")]
        [SerializeField] private List<Transform> spawnPoints = new List<Transform>();

        [Header("Visuals")]
        [SerializeField] private Renderer postRenderer;
        [SerializeField] private Material redMaterial;
        [SerializeField] private Material blueMaterial;
        [SerializeField] private Material neutralMaterial;
        [Tooltip("Flat disc showing the capture zone. Sized to captureRadius and tinted by owner at runtime.")]
        [SerializeField] private Renderer zoneRenderer;
        [SerializeField] private Color zoneRedColor = new Color(1f, 0.25f, 0.2f, 0.22f);
        [SerializeField] private Color zoneBlueColor = new Color(0.25f, 0.55f, 1f, 0.22f);
        [SerializeField] private Color zoneNeutralColor = new Color(0.85f, 0.85f, 0.85f, 0.16f);
        [Tooltip("Colour-blind palette: replaces red on the post body and the zone disc.")]
        [SerializeField] private Color colorBlindRedColor = new Color(0.95f, 0.55f, 0.1f, 1f);

        [Header("Audio")]
        [SerializeField] private AudioSource postAudioSource;
        [SerializeField] private AudioClip captureSoundClip;
        [SerializeField] private AudioClip neutralizeSoundClip;

        // Networked state
        public NetworkVariable<FixedString32Bytes> postIdentifier = new NetworkVariable<FixedString32Bytes>(
            "Post",
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<Team> currentOwner = new NetworkVariable<Team>(
            Team.Neutral,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<Team> capturingTeam = new NetworkVariable<Team>(
            Team.Neutral,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<float> captureProgress = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server); // 0.0 to 1.0

        public NetworkVariable<bool> isContested = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>The owner's enemies are inside the zone (attacking or contesting): nobody may spawn here.</summary>
        public NetworkVariable<bool> isSpawnBlocked = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public string PostName => postIdentifier.Value.ToString();
        public Team CurrentOwner => currentOwner.Value;
        public float CaptureProgress => captureProgress.Value;
        public bool IsContested => isContested.Value;
        public bool IsSpawnBlocked => isSpawnBlocked.Value;
        public float CaptureRadius => captureRadius;

        public event Action<ConquestCommandPost, Team> OnOwnerChanged;

        // Server scratch lists of who is standing in the zone this frame, for capture credit.
        private readonly List<ConquestPlayer> m_RedInZone = new List<ConquestPlayer>();
        private readonly List<ConquestPlayer> m_BlueInZone = new List<ConquestPlayer>();

        public override void OnNetworkSpawn()
        {
            currentOwner.OnValueChanged += HandleOwnerChanged;
            UpdateVisuals(currentOwner.Value);
            m_Settings = Game.Conquest.Settings.ConquestSettingsManager.Instance;
            if (m_Settings != null) m_Settings.OnSettingsChanged += HandleSettingsChanged;

            if (IsServer)
            {
                postIdentifier.Value = postName;
                currentOwner.Value = initialOwner;
                captureProgress.Value = initialOwner == Team.Neutral ? 0f : 1f;
                capturingTeam.Value = initialOwner;
            }
        }

        public override void OnNetworkDespawn()
        {
            currentOwner.OnValueChanged -= HandleOwnerChanged;
            if (m_Settings != null) m_Settings.OnSettingsChanged -= HandleSettingsChanged;
            m_Settings = null;
        }

        private Game.Conquest.Settings.ConquestSettingsManager m_Settings;

        private void HandleSettingsChanged(Game.Conquest.Settings.ConquestSettingsData settings)
        {
            UpdateVisuals(currentOwner.Value);
        }

        private void HandleOwnerChanged(Team oldOwner, Team newOwner)
        {
            UpdateVisuals(newOwner);
            OnOwnerChanged?.Invoke(this, newOwner);
            PlayCaptureAudio(newOwner);
        }

        private void PlayCaptureAudio(Team owner)
        {
            if (postAudioSource == null || !postAudioSource.isActiveAndEnabled) return;

            if (owner == Team.Neutral)
            {
                if (neutralizeSoundClip != null) postAudioSource.PlayOneShot(neutralizeSoundClip, 0.8f);
            }
            else
            {
                if (captureSoundClip != null) postAudioSource.PlayOneShot(captureSoundClip, 0.9f);
            }
        }

        private void Update()
        {
            if (!IsServer) return;

            EvaluateCaptureState();
        }

        /// <summary>True if a point is inside the capture zone: a cylinder around the post, not a sphere.</summary>
        public bool IsInCaptureZone(Vector3 position)
        {
            Vector3 offset = position - transform.position;
            if (Mathf.Abs(offset.y) > captureHeight) return false;
            offset.y = 0f;
            return offset.sqrMagnitude <= captureRadius * captureRadius;
        }

        private void EvaluateCaptureState()
        {
            // Count each living player once. A physics query counted every collider, so bots
            // (CharacterController + CapsuleCollider) captured twice as fast as humans.
            m_RedInZone.Clear();
            m_BlueInZone.Clear();

            foreach (var player in ConquestPlayer.All)
            {
                if (player == null || player.Health == null || player.Health.IsDead) continue;
                if (!IsInCaptureZone(player.transform.position)) continue;

                if (player.CurrentTeam == Team.Red) m_RedInZone.Add(player);
                else if (player.CurrentTeam == Team.Blue) m_BlueInZone.Add(player);
            }
            int redPlayers = m_RedInZone.Count;
            int bluePlayers = m_BlueInZone.Count;

            Team owner = currentOwner.Value;
            int ownerEnemies = owner == Team.Red ? bluePlayers : (owner == Team.Blue ? redPlayers : 0);
            isSpawnBlocked.Value = ownerEnemies > 0;

            // Determine if contested
            bool contested = redPlayers > 0 && bluePlayers > 0;
            isContested.Value = contested;

            if (contested)
            {
                // Both teams present: capture progress is frozen
                return;
            }

            Team dominantTeam = Team.Neutral;
            int dominantCount = 0;
            if (redPlayers > 0)
            {
                dominantTeam = Team.Red;
                dominantCount = redPlayers;
            }
            else if (bluePlayers > 0)
            {
                dominantTeam = Team.Blue;
                dominantCount = bluePlayers;
            }

            if (dominantTeam == Team.Neutral)
            {
                RecoverIdleProgress();
                return;
            }

            float speed = (1.0f / captureTime) * Mathf.Max(1, dominantCount) * Time.deltaTime;

            if (currentOwner.Value == dominantTeam)
            {
                // Dominant team already owns this post: reinforce to 1.0
                if (captureProgress.Value < 1.0f)
                {
                    captureProgress.Value = Mathf.Min(1.0f, captureProgress.Value + speed);
                    capturingTeam.Value = dominantTeam;
                }
            }
            else if (currentOwner.Value != Team.Neutral)
            {
                // Opposing team is neutralizing current owner
                captureProgress.Value -= speed;
                if (captureProgress.Value <= 0f)
                {
                    captureProgress.Value = 0f;
                    currentOwner.Value = Team.Neutral;
                    capturingTeam.Value = dominantTeam;
                    CreditCapture(dominantTeam);
                    Debug.Log($"[CommandPost {PostName}] Post neutralized by {dominantTeam}!");
                }
            }
            else if (capturingTeam.Value != dominantTeam && captureProgress.Value > 0f)
            {
                // Neutral post part-captured by the other team: undo their progress first,
                // so neither team inherits the other's work.
                captureProgress.Value = Mathf.Max(0f, captureProgress.Value - speed);
                if (captureProgress.Value <= 0f)
                {
                    capturingTeam.Value = dominantTeam;
                }
            }
            else
            {
                // Point is Neutral: dominant team captures it
                capturingTeam.Value = dominantTeam;
                captureProgress.Value += speed;

                if (captureProgress.Value >= 1.0f)
                {
                    captureProgress.Value = 1.0f;
                    currentOwner.Value = dominantTeam;
                    CreditCapture(dominantTeam);
                    Debug.Log($"[CommandPost {PostName}] Captured by {dominantTeam}!");
                }
            }
        }

        private void CreditCapture(Team team)
        {
            ConquestMatchManager.Instance?.OnPostCaptured(team == Team.Red ? m_RedInZone : m_BlueInZone);
        }

        // Nobody on the post: an owned post refills toward 1, a neutral one empties toward 0.
        private void RecoverIdleProgress()
        {
            if (idleRecoveryRate <= 0f) return;

            float step = idleRecoveryRate * Time.deltaTime;
            if (currentOwner.Value != Team.Neutral)
            {
                if (captureProgress.Value < 1f)
                {
                    captureProgress.Value = Mathf.Min(1f, captureProgress.Value + step);
                    capturingTeam.Value = currentOwner.Value;
                }
            }
            else if (captureProgress.Value > 0f)
            {
                captureProgress.Value = Mathf.Max(0f, captureProgress.Value - step);
                if (captureProgress.Value <= 0f) capturingTeam.Value = Team.Neutral;
            }
        }

        public void UpdateVisuals(Team owner)
        {
            UpdateZoneVisual(owner);
            if (postRenderer == null) return;
            Material mat = (owner == Team.Red) ? redMaterial : (owner == Team.Blue ? blueMaterial : neutralMaterial);
            if (mat != null)
            {
                postRenderer.sharedMaterial = mat;
            }
            // Colour-blind red goes through a property block so the shared material stays untouched.
            if (m_BodyBlock == null) m_BodyBlock = new MaterialPropertyBlock();
            postRenderer.GetPropertyBlock(m_BodyBlock);
            if (owner == Team.Red && Game.Conquest.Settings.ConquestSettingsManager.ColorBlindTeams) m_BodyBlock.SetColor(s_BaseColorId, colorBlindRedColor);
            else m_BodyBlock.Clear();
            postRenderer.SetPropertyBlock(m_BodyBlock);
        }

        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock m_ZoneBlock;
        private MaterialPropertyBlock m_BodyBlock;

        // The disc matches the real capture radius (a unit cylinder is 1 m across) and takes the
        // owner's colour through a property block, so all posts share one material asset.
        private void UpdateZoneVisual(Team owner)
        {
            if (zoneRenderer == null) return;
            Transform zone = zoneRenderer.transform;
            Vector3 parentScale = zone.parent != null ? zone.parent.lossyScale : Vector3.one;
            zone.localScale = new Vector3(captureRadius * 2f / Mathf.Max(0.01f, parentScale.x), zone.localScale.y,
                                          captureRadius * 2f / Mathf.Max(0.01f, parentScale.z));

            if (m_ZoneBlock == null) m_ZoneBlock = new MaterialPropertyBlock();
            zoneRenderer.GetPropertyBlock(m_ZoneBlock);
            Color red = Game.Conquest.Settings.ConquestSettingsManager.ColorBlindTeams ? new Color(colorBlindRedColor.r, colorBlindRedColor.g, colorBlindRedColor.b, zoneRedColor.a) : zoneRedColor;
            m_ZoneBlock.SetColor(s_BaseColorId, owner == Team.Red ? red : owner == Team.Blue ? zoneBlueColor : zoneNeutralColor);
            zoneRenderer.SetPropertyBlock(m_ZoneBlock);
        }

        public bool GetSpawnPoint(out Vector3 position, out Quaternion rotation)
        {
            if (spawnPoints != null && spawnPoints.Count > 0)
            {
                Transform pt = spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Count)];
                if (pt != null)
                {
                    position = pt.position;
                    rotation = pt.rotation;
                    if (UnityEngine.Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 15f))
                    {
                        position = hit.point + Vector3.up * 0.15f;
                    }
                    return true;
                }
            }

            // Fallback: slight offset from post center
            Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * (captureRadius * 0.5f);
            position = transform.position + new Vector3(randomCircle.x, 0.5f, randomCircle.y);
            if (UnityEngine.Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out RaycastHit fallbackHit, 15f))
            {
                position = fallbackHit.point + Vector3.up * 0.15f;
            }
            rotation = transform.rotation;
            return true;
        }

        /// <summary>
        /// Returns a safe position inside the capture radius away from the central pillar.
        /// </summary>
        public Vector3 GetCaptureTargetPosition(Vector3 approachingFrom)
        {
            Vector3 dir = (approachingFrom - transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.1f)
            {
                dir = Vector3.forward;
            }
            dir.Normalize();

            // Stand at 45% - 60% of capture radius away from the center pillar
            float standOffDistance = captureRadius * 0.55f;
            Vector3 target = transform.position + dir * standOffDistance;
            target.y = transform.position.y;

            if (NavMesh.SamplePosition(target, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
            {
                return hit.position;
            }

            return target;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = currentOwner.Value == Team.Red ? Color.red : (currentOwner.Value == Team.Blue ? Color.blue : Color.white);
            Gizmos.DrawWireSphere(transform.position, captureRadius);
        }
    }
}
