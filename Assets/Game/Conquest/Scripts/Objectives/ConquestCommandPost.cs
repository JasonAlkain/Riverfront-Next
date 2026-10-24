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
        [Tooltip("A team's home base: always owned by Initial Owner, never captured or neutralised, and always open " +
                 "for that team to spawn at. It doesn't count toward holding every post, and bots don't attack it.")]
        [SerializeField] private bool isHomeBase = false;

        [Header("Capture Rules")]
        [SerializeField] private float captureRadius = 8.0f;
        [Tooltip("How far above or below the post a player still counts, so a roof or a floor below doesn't capture.")]
        [SerializeField] private float captureHeight = 4.0f;
        [Tooltip("Placeholder: seconds for one player to take a neutral post to fully owned.")]
        [SerializeField] private float captureTime = 10.0f;
        [Tooltip("Placeholder: seconds for one player to drain an enemy post to neutral.")]
        [SerializeField] private float neutralizeTime = 10.0f;
        [Tooltip("Placeholder: within the crowd, each extra player adds this share of what the one before added. " +
                 "At 0.75, two players capture 1.75x as fast, three 2.3x and four 2.7x.")]
        [Range(0f, 1f)]
        [SerializeField] private float crowdFalloff = 0.75f;
        [Tooltip("Placeholder: how many players count toward the diminishing curve above.")]
        [SerializeField] private int crowdSize = 4;
        [Tooltip("Placeholder: what each player past the crowd adds (1 = as much as the first player). No cap.")]
        [SerializeField] private float crowdTailGain = 0.1f;
        [Tooltip("Progress per second an empty post drifts back toward its owner (or toward neutral). 0 holds partial progress.")]
        [SerializeField] private float idleRecoveryRate = 0f;

        [Header("Spawn Locations")]
        [SerializeField] private List<Transform> spawnPoints = new List<Transform>();
        [Tooltip("Placeholder: relative chance of this post when a spawn names no post (bots, fallbacks). 0 never picks it that way.")]
        [SerializeField] private float spawnWeight = 1f;
        [Tooltip("Placeholder: a spawn point with a living player closer than this many metres is skipped while another one is clear.")]
        [SerializeField] private float spawnClearance = 1.2f;

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
        [Tooltip("The beacon (the Post Renderer) sinks to this share of its height as the post drains, and rises as it is captured.")]
        [Range(0f, 1f)]
        [SerializeField] private float beaconLowFraction = 0.35f;
        [Tooltip("Placeholder: how fast the beacon pulses while the post is contested.")]
        [SerializeField] private float contestedPulseSpeed = 6f;

        [Header("Home Base Look")]
        [Tooltip("Shown only on a home base, for example a team banner. Hidden on every other post. Its renderers take the owner's material.")]
        [SerializeField] private GameObject homeBaseMarker;
        [Tooltip("Placeholder: a home base's beacon is this much bigger than a normal post's.")]
        [SerializeField] private float homeBaseBeaconScale = 1.8f;
        [Tooltip("Placeholder: opacity of a home base's zone disc (normal posts use the zone colours' own alpha).")]
        [Range(0f, 1f)]
        [SerializeField] private float homeBaseZoneAlpha = 0.4f;

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

        /// <summary>Cached: the HUD reads it every frame, and FixedString.ToString allocates.</summary>
        public string PostName
        {
            get
            {
                if (m_PostNameCache == null || m_PostNameSource != postIdentifier.Value)
                {
                    m_PostNameSource = postIdentifier.Value;
                    m_PostNameCache = m_PostNameSource.ToString();
                }
                return m_PostNameCache;
            }
        }
        private string m_PostNameCache;
        private FixedString32Bytes m_PostNameSource;
        public Team CurrentOwner => currentOwner.Value;
        public float CaptureProgress => captureProgress.Value;
        public bool IsContested => isContested.Value;
        public bool IsSpawnBlocked => isSpawnBlocked.Value;
        public float CaptureRadius => captureRadius;
        /// <summary>A team's uncapturable home base.</summary>
        public bool IsHomeBase => isHomeBase;
        /// <summary>Can be captured or neutralised: every post except a home base.</summary>
        public bool IsCapturable => !isHomeBase;
        public float SpawnWeight => Mathf.Max(0f, spawnWeight);

        public event Action<ConquestCommandPost, Team> OnOwnerChanged;

        // Server scratch lists of who is standing in the zone this frame, for capture credit.
        private readonly List<ConquestPlayer> m_RedInZone = new List<ConquestPlayer>();
        private readonly List<ConquestPlayer> m_BlueInZone = new List<ConquestPlayer>();

        // The beacon's authored pose; it moves with capture progress from there.
        private Transform m_Beacon;
        private Vector3 m_BeaconRest;
        private Vector3 m_BeaconScale;
        private Renderer[] m_MarkerRenderers;

        private void Awake()
        {
            if (postRenderer != null)
            {
                m_Beacon = postRenderer.transform;
                m_BeaconRest = m_Beacon.localPosition;
                m_BeaconScale = m_Beacon.localScale * (isHomeBase ? homeBaseBeaconScale : 1f);
                m_Beacon.localScale = m_BeaconScale;
            }
            if (homeBaseMarker != null)
            {
                homeBaseMarker.SetActive(isHomeBase);
                if (isHomeBase) m_MarkerRenderers = homeBaseMarker.GetComponentsInChildren<Renderer>(true);
            }
            if (isHomeBase && initialOwner == Team.Neutral)
            {
                Debug.LogWarning($"[CommandPost {postName}] A home base needs a team as Initial Owner.", this);
            }
        }

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
            // A home base keeps the owner and full progress it spawned with.
            if (IsServer && !isHomeBase) EvaluateCaptureState();
            UpdateBeacon();
        }

        // Every peer: the beacon stands as tall as the post is held and pulses while contested.
        private void UpdateBeacon()
        {
            if (m_Beacon == null || isHomeBase) return;

            Vector3 position = m_BeaconRest;
            position.y = Mathf.Lerp(m_BeaconRest.y * beaconLowFraction, m_BeaconRest.y, Mathf.Clamp01(captureProgress.Value));
            float pulse = isContested.Value && !Game.Conquest.Settings.ConquestSettingsManager.ReduceMotion
                ? 1f + 0.15f * Mathf.Sin(Time.time * contestedPulseSpeed)
                : 1f;
            if (m_Beacon.localPosition != position) m_Beacon.localPosition = position;
            Vector3 scale = m_BeaconScale * pulse;
            if (m_Beacon.localScale != scale) m_Beacon.localScale = scale;
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

            // Diminishing returns: the first few players matter most; past the crowd each adds a little.
            float crowd = ConquestCaptureRules.CrowdMultiplier(dominantCount, crowdFalloff, crowdSize, crowdTailGain) * Time.deltaTime;
            float captureStep = crowd / Mathf.Max(0.01f, captureTime);
            float neutralizeStep = crowd / Mathf.Max(0.01f, neutralizeTime);

            if (currentOwner.Value == dominantTeam)
            {
                // Dominant team already owns this post: reinforce to 1.0
                if (captureProgress.Value < 1.0f)
                {
                    captureProgress.Value = Mathf.Min(1.0f, captureProgress.Value + captureStep);
                    capturingTeam.Value = dominantTeam;
                }
            }
            else if (currentOwner.Value != Team.Neutral)
            {
                // Opposing team is neutralizing current owner
                captureProgress.Value -= neutralizeStep;
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
                captureProgress.Value = Mathf.Max(0f, captureProgress.Value - neutralizeStep);
                if (captureProgress.Value <= 0f)
                {
                    capturingTeam.Value = dominantTeam;
                }
            }
            else
            {
                // Point is Neutral: dominant team captures it
                capturingTeam.Value = dominantTeam;
                captureProgress.Value += captureStep;

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

            if (m_MarkerRenderers == null) return;
            for (int i = 0; i < m_MarkerRenderers.Length; i++)
            {
                if (mat != null) m_MarkerRenderers[i].sharedMaterial = mat;
                m_MarkerRenderers[i].SetPropertyBlock(m_BodyBlock);
            }
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
            Color zoneColor = owner == Team.Red ? red : owner == Team.Blue ? zoneBlueColor : zoneNeutralColor;
            // A home base's disc is more solid, so it reads as a base and not a post to take.
            if (isHomeBase) zoneColor.a = homeBaseZoneAlpha;
            m_ZoneBlock.SetColor(s_BaseColorId, zoneColor);
            zoneRenderer.SetPropertyBlock(m_ZoneBlock);
        }

        public bool GetSpawnPoint(out Vector3 position, out Quaternion rotation)
        {
            if (spawnPoints != null && spawnPoints.Count > 0)
            {
                Transform pt = PickClearSpawnPoint();
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

        // A random spawn point, skipping ones someone is standing on while another is clear, so
        // simultaneous respawns don't stack players inside each other.
        private Transform PickClearSpawnPoint()
        {
            int count = spawnPoints.Count;
            int start = UnityEngine.Random.Range(0, count);
            Transform fallback = null;
            for (int i = 0; i < count; i++)
            {
                Transform pt = spawnPoints[(start + i) % count];
                if (pt == null) continue;
                if (fallback == null) fallback = pt;
                if (!IsOccupied(pt.position)) return pt;
            }
            return fallback;
        }

        private bool IsOccupied(Vector3 point)
        {
            float sqrClearance = spawnClearance * spawnClearance;
            var players = ConquestPlayer.All;
            for (int i = 0; i < players.Count; i++)
            {
                ConquestPlayer p = players[i];
                if (p == null || p.Health == null || p.Health.IsDead) continue;
                if ((p.transform.position - point).sqrMagnitude < sqrClearance) return true;
            }
            return false;
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
