using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Controls;
using Game.Conquest.Player;
using Game.Conquest.Classes;
using Game.Conquest.Gameplay;
using Game.Conquest.UI;
using Game.Conquest.Audio;

namespace Game.Conquest.Combat
{
    /// <summary>
    /// A combatant's weapon. Shots are travelling bolts simulated on the server
    /// (ConquestBoltSimulator); every peer draws them (ConquestBoltPool). How the weapon keeps
    /// firing depends on the team's faction: a magazine and reload, or heat and an overheat lock.
    /// The server owns ammo and heat; the shooting player's own peer predicts them
    /// (ConquestSustainPredictor) so the trigger and the HUD respond at once.
    /// </summary>
    public class ConquestWeapon : NetworkBehaviour
    {
        [Header("Weapon Stats")]
        [SerializeField] private int damagePerShot = 22;
        [SerializeField] private float fireRate = 0.14f;
        [SerializeField] private float maxRange = 100.0f;
        [SerializeField] private int bulletsPerShot = 1;
        [SerializeField] private float spreadAngle = 0.5f;
        [SerializeField] private int magazineCapacity = 32;
        [SerializeField] private float reloadDuration = 1.6f;
        [SerializeField] private bool isAutomatic = true;
        [SerializeField] private LayerMask hitLayers = ConquestPhysics.ShotMask;

        [Header("Bolt and heat (set from the class)")]
        [SerializeField] private float boltSpeed = 120f;
        [SerializeField] private float boltRadius = 0f;
        [SerializeField] private int shotsToOverheat = 30;
        [SerializeField] private float coolDelaySeconds = 0.25f;
        [SerializeField] private float fullCoolSeconds = 1.6f;
        [SerializeField] private float overheatLockoutSeconds = 1.6f;
        [SerializeField] private float heatAfterOverheat = 0f;

        [Header("Special Abilities")]
        [SerializeField] private bool hasZoom = false;
        [SerializeField] private float zoomMagnification = 1.25f;
        [SerializeField] private bool hasScope = false;
        [SerializeField] private bool hasSupportStation = false;
        [SerializeField] private GameObject supportStationPrefab;
        [SerializeField] private float stationCooldown = 15.0f;
        [Tooltip("Placeholder: most stations one team may have out at once (each player still has at most one).")]
        [SerializeField] private int maxStationsPerTeam = 3;

        [Header("Server Validation")]
        [Tooltip("Shots may arrive this much earlier than the fire rate allows (network jitter) without being dropped. Also how early a lock may be ended for an arriving shot.")]
        [SerializeField] private float fireTimingTolerance = 0.1f;
        [Tooltip("Max distance between the client's ray origin and the server's view of the player's camera.")]
        [SerializeField] private float maxRayOriginOffset = 3.0f;
        [Tooltip("Distance in front of the player where the server places a deployed station.")]
        [SerializeField] private float stationDeployDistance = 1.5f;

        [Header("Visuals & Audio")]
        [SerializeField] private Transform muzzlePoint;
        [SerializeField] private GameObject muzzleFlashPrefab;
        [Tooltip("Impact on world geometry and stations.")]
        [SerializeField] private GameObject hitImpactPrefab;
        [Tooltip("Impact on a player or bot. Falls back to the surface impact when empty.")]
        [SerializeField] private GameObject characterImpactPrefab;
        [SerializeField] private float muzzleFlashSeconds = 0.06f;
        [SerializeField] private float impactSeconds = 0.6f;
        [SerializeField] private AudioSource weaponAudioSource;
        [SerializeField] private AudioClip fireSoundClip;
        [SerializeField] private AudioClip reloadSoundClip;
        [SerializeField] private AudioClip targetHitClip;
        [Tooltip("Optional: played instead of the hit sound when the shot killed. Empty uses the hit sound.")]
        [SerializeField] private AudioClip killConfirmClip;
        [SerializeField] private float firePitchMin = 0.95f;
        [SerializeField] private float firePitchMax = 1.05f;
        [SerializeField] private float fireVolume = 0.40f;

        // Everyone hears a reload or a vent, so these two flags are public; the numbers are owner-only.
        public NetworkVariable<bool> isReloading = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> isOverheated = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>The server's ammo, heat and lock, sent to the shooting player only, on events.</summary>
        public NetworkVariable<WeaponOwnerState> ownerState = new NetworkVariable<WeaponOwnerState>(
            default,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        private enum RejectReason : byte { CannotShoot = 0, Invalid = 1, Resync = 2 }

        private ConquestPlayerMovement m_Movement;
        private ConquestHealth m_Health;
        private ConquestPlayer m_Player;
        private ConquestPlayerAnimator m_Animator;
        private float m_NextFireTime;
        private float m_NextStationDeployTime;
        private readonly ConquestSustainPredictor m_Predictor = new ConquestSustainPredictor();

        // Server-side state. Kept separate from the owner's prediction because on a host the
        // same component is both owner and server.
        private float m_ServerNextFireTime;
        private float m_ServerNextStationTime;
        private NetworkObject m_ServerActiveStation;
        private ClientRpcParams m_OwnerRpcParams;
        private int m_ServerAmmo;
        private float m_ServerHeat;
        private double m_ServerCoolFrom;
        private WeaponLock m_ServerLock;
        private double m_ServerLockStart;
        private double m_ServerLockEnd;
        private byte m_ServerLockCount;
        private byte m_ServerEpoch;
        private ushort m_ServerProcessed;
        private bool m_OwnerStateDirty;
        private bool m_PendingHit;
        private bool m_PendingKill;
        private bool m_IsZoomed = false;

        /// <summary>The team's weapon model, resolved from the replicated team every time (never cached).</summary>
        public ConquestWeaponModel Model
        {
            get
            {
                ConquestFactionData faction = Faction;
                return faction != null ? faction.weaponModel : ConquestWeaponModel.Magazine;
            }
        }

        public bool UsesHeat => Model == ConquestWeaponModel.Overheat;

        private ConquestFactionData Faction => m_Player != null ? m_Player.CurrentFaction : null;

        private ConquestBoltSettings Settings
        {
            get
            {
                ConquestClassRoster roster = m_Player != null ? m_Player.ClassRoster : null;
                return roster != null ? roster.BoltSettings : ConquestBoltSettings.Fallback;
            }
        }

        private SustainParams Params => new SustainParams(Model, magazineCapacity, reloadDuration, shotsToOverheat,
            coolDelaySeconds, fullCoolSeconds, overheatLockoutSeconds, heatAfterOverheat);

        // Read on the shooting player's peer from the prediction, and on the server from the authority.
        public int CurrentAmmo => PredictsLocally ? m_Predictor.Ammo : m_ServerAmmo;
        public int MagazineCapacity => magazineCapacity;
        public bool IsReloading => PredictsLocally ? m_Predictor.IsReloading : isReloading.Value;
        public bool IsOverheated => PredictsLocally ? m_Predictor.IsOverheated : isOverheated.Value;
        /// <summary>Reloading or overheated: the weapon can't fire right now.</summary>
        public bool IsWeaponLocked => IsReloading || IsOverheated;
        /// <summary>Heat gauge 0..1 on the shooting player's peer (predicted) or the server.</summary>
        public float Heat01 => PredictsLocally ? m_Predictor.Heat01(Params, Time.timeAsDouble) : ServerHeat01;
        /// <summary>Server: the authoritative heat gauge 0..1. Bots read it to pace their fire.</summary>
        public float ServerHeat01 => ConquestHeatModel.Display01(m_ServerHeat, m_ServerCoolFrom, Time.timeAsDouble, Params,
            m_ServerLock, m_ServerLockStart, m_ServerLockEnd);
        public bool HasZoom => hasZoom;
        /// <summary>Random cone, in degrees, each pellet can stray from the aim; the HUD crosshair opens to match.</summary>
        public float SpreadAngle => spreadAngle;
        public bool IsZoomed => m_IsZoomed;
        public bool HasScope => hasScope;
        /// <summary>Zoomed with a scope weapon: the HUD swaps the crosshair for the scope overlay.</summary>
        public bool IsScoped => m_IsZoomed && hasScope;
        public bool HasSupportStation => hasSupportStation;
        /// <summary>Server: when this weapon last fired (Time.time). Bots use it to hear nearby gunfire.</summary>
        public float ServerLastShotTime { get; private set; } = float.NegativeInfinity;
        public float BoltSpeed => boltSpeed;
        /// <summary>Seconds a bolt flies before running out of range.</summary>
        public float BoltLifetime => boltSpeed > 0f ? maxRange / boltSpeed : 0f;
        /// <summary>Where a bot's bolts start.</summary>
        public Vector3 AIFireOrigin => muzzlePoint != null ? muzzlePoint.position : transform.position + Vector3.up * 1.5f;

        private void Awake()
        {
            m_Movement = GetComponent<ConquestPlayerMovement>();
            m_Health = GetComponent<ConquestHealth>();
            m_Player = GetComponent<ConquestPlayer>();
            m_Animator = GetComponent<ConquestPlayerAnimator>();
        }

        public override void OnNetworkDespawn()
        {
            isReloading.OnValueChanged -= HandleReloadingChanged;
            isOverheated.OnValueChanged -= HandleOverheatedChanged;
            ownerState.OnValueChanged -= HandleOwnerStateChanged;
            if (IsServer && m_Health != null)
            {
                m_Health.OnDied -= HandleServerDied;
            }

            // A player who leaves (or is cleared at match end) takes their support station with them.
            if (IsServer && m_ServerActiveStation != null && m_ServerActiveStation.IsSpawned)
            {
                m_ServerActiveStation.Despawn(true);
            }
            m_ServerActiveStation = null;
        }

        public override void OnNetworkSpawn()
        {
            isReloading.OnValueChanged += HandleReloadingChanged;
            isOverheated.OnValueChanged += HandleOverheatedChanged;
            ownerState.OnValueChanged += HandleOwnerStateChanged;

            if (IsServer)
            {
                // Cached so a hit confirmation doesn't allocate a target list every shot.
                m_OwnerRpcParams = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } } };
                if (m_Health != null) m_Health.OnDied += HandleServerDied;
                ServerResetAmmo();
            }

            if (PredictsLocally)
            {
                m_Predictor.Reset(Params);
                if (!IsServer) m_Predictor.OnSnapshot(Params, ownerState.Value, Time.timeAsDouble, OneWaySeconds());
                ConquestBoltSettings settings = Settings;
                ConquestBoltPool.Configure(settings.convergeDistance, settings.catchUpSeconds);
            }
        }

        public void ApplyClass(ConquestClassData data)
        {
            if (data == null) return;

            damagePerShot = data.damagePerShot;
            fireRate = data.fireRate;
            maxRange = data.maxRange;
            bulletsPerShot = Mathf.Clamp(data.bulletsPerShot, 1, 254);
            spreadAngle = data.spreadAngle;
            magazineCapacity = Mathf.Clamp(data.magazineCapacity, 1, 255);
            reloadDuration = data.reloadDuration;
            isAutomatic = data.isAutomatic;
            boltSpeed = Mathf.Max(0.1f, data.boltSpeed);
            boltRadius = Mathf.Max(0f, data.boltRadius);
            shotsToOverheat = Mathf.Clamp(data.shotsToOverheat, 1, 255);
            coolDelaySeconds = data.coolDelaySeconds;
            fullCoolSeconds = data.fullCoolSeconds;
            overheatLockoutSeconds = data.overheatLockoutSeconds;
            heatAfterOverheat = data.heatAfterOverheat;
            hasZoom = data.hasZoom;
            zoomMagnification = Mathf.Max(1f, data.zoomMagnification);
            hasScope = data.hasScope;
            hasSupportStation = data.hasSupportStation;

            if (data.fireSound != null) fireSoundClip = data.fireSound;
            if (data.reloadSound != null) reloadSoundClip = data.reloadSound;
            firePitchMin = data.firePitchMin;
            firePitchMax = data.firePitchMax;
            fireVolume = data.fireVolume;

            // Also runs on every respawn (ConquestPlayer.HandleRespawn), which is what refills the
            // magazine and cools the weapon.
            ServerResetAmmo();
            if (PredictsLocally) m_Predictor.Reset(Params);
        }

        private void HandleServerDied(ulong killerId)
        {
            ServerResetAmmo();
        }

        private void Update()
        {
            if (IsServer) ServerTickLock();

            // The host owns every bot, so IsOwner alone would let the host's keys drive bot weapons.
            if (!PredictsLocally) return;
            m_Predictor.Tick(Params, Time.timeAsDouble);
            bool canAct = !(m_Health != null && m_Health.IsDead) && !IsMatchOver &&
                          !(ConquestPauseMenu.Instance != null && ConquestPauseMenu.Instance.IsPaused);

            // Always runs, so dying or switching off a scoped class zooms back out.
            HandleZoom(canAct);
            if (canAct) HandleInput();
        }

        private void LateUpdate()
        {
            if (!IsServer || !IsSpawned) return;

            // Every pellet of a shot that connects reports here; the shooter hears one confirmation.
            if (m_PendingHit)
            {
                ConfirmHitClientRpc(m_PendingKill, m_OwnerRpcParams);
                m_PendingHit = false;
                m_PendingKill = false;
            }
            if (m_OwnerStateDirty) ServerWriteOwnerState();
        }

        // Firing and stations stop when the match ends, on the owner and again on the server.
        private static bool IsMatchOver =>
            ConquestMatchManager.Instance != null && ConquestMatchManager.Instance.IsMatchOver;

        private bool IsBot => m_Player != null && m_Player.isBot;

        /// <summary>True on the human owner's peer, which reads input and draws its own shots ahead of the server.</summary>
        private bool PredictsLocally => IsOwner && !IsBot;

        private void HandleInput()
        {
            if (Cursor.lockState != CursorLockMode.Locked) return;

            SustainParams p = Params;
            double now = Time.timeAsDouble;
            bool fireRequested = isAutomatic ? ConquestInput.FireHeld : ConquestInput.FirePressed;

            if (fireRequested && Time.time >= m_NextFireTime)
            {
                if (m_Predictor.CanFire(p, now)) TryFireLocal(p, now);
                // An empty magazine the predictor didn't lock itself (adopted from the server): reload.
                else if (!p.UsesHeat && m_Predictor.Lock == WeaponLock.None && m_Predictor.Ammo <= 0) TryStartReload(p, now);
            }

            // Heat weapons never reload; they vent by themselves.
            if (!p.UsesHeat && ConquestInput.ReloadPressed)
            {
                TryStartReload(p, now);
            }

            if (hasSupportStation && ConquestInput.DeployPressed)
            {
                TryDeployStation();
            }
        }

        private void HandleZoom(bool canAct)
        {
            if (m_Movement == null || m_Movement.PlayerCamera == null) return;

            m_IsZoomed = canAct && hasZoom && ConquestInput.AimHeld;

            // The unzoomed FOV follows the settings, even when they change mid-match; zoom divides
            // the view's half-angle tangent, so the magnification is the same at any base FOV.
            float baseFOV = m_Movement.BaseFieldOfView;
            float targetFOV = m_IsZoomed
                ? 2f * Mathf.Atan(Mathf.Tan(baseFOV * 0.5f * Mathf.Deg2Rad) / zoomMagnification) * Mathf.Rad2Deg
                : baseFOV;
            m_Movement.PlayerCamera.fieldOfView = Mathf.Lerp(m_Movement.PlayerCamera.fieldOfView, targetFOV, Time.deltaTime * 12.0f);
        }

        private void TryFireLocal(in SustainParams p, double now)
        {
            m_NextFireTime = Time.time + fireRate;

            Camera cam = m_Movement.PlayerCamera;
            if (cam == null) return;
            Ray baseRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            // The shot number is the server's too, so the pellets drawn here are the ones it simulates.
            ushort seq = m_Predictor.OnShot(p, now);
            Vector3 muzzle = muzzlePoint != null ? muzzlePoint.position : baseRay.origin;
            SpawnShotVisuals(baseRay.origin, baseRay.direction, muzzle, seq, true, now);

            PlayMuzzleFlash();
            PlayFireAudio();
            m_Animator?.TriggerFire();

            FireServerRpc(baseRay.origin, baseRay.direction, seq);
        }

        private void SpawnShotVisuals(Vector3 origin, Vector3 direction, Vector3 muzzle, ushort seq, bool own, double t0)
        {
            ConquestFactionData faction = Faction;
            if (faction == null) return;
            Material material = faction.BoltMaterial(Game.Conquest.Settings.ConquestSettingsManager.ColorBlindTeams);
            uint seed = ConquestBallistics.Seed(NetworkObjectId, seq);
            for (int i = 0; i < bulletsPerShot; i++)
            {
                var visual = new BoltVisual
                {
                    origin = origin,
                    dir = ConquestBallistics.Pellet(direction, spreadAngle, seed, i),
                    muzzleOffset = muzzle - origin,
                    speed = boltSpeed,
                    maxDistance = maxRange,
                    width = faction.boltWidth,
                    length = faction.boltLength,
                    material = material,
                    own = own,
                    t0 = t0,
                    shooterId = NetworkObjectId,
                    seq = seq,
                    pellet = (byte)i,
                    ignoreRoot = transform,
                    surfaceImpact = hitImpactPrefab,
                    characterImpact = characterImpactPrefab,
                    impactSeconds = impactSeconds
                };
                ConquestBoltPool.Spawn(visual);
            }
        }

        private Vector3 ApplySpread(Vector3 forward, float angle)
        {
            if (angle <= 0.01f) return forward;
            Quaternion spreadRot = Quaternion.Euler(
                Random.Range(-angle, angle),
                Random.Range(-angle, angle),
                0f);
            return (Quaternion.LookRotation(forward) * spreadRot) * Vector3.forward;
        }

        [ServerRpc]
        private void FireServerRpc(Vector3 rayOrigin, Vector3 rayDirection, ushort shotSeq)
        {
            // Numbered before anything else, accepted or not, so the seed is always the server's.
            ushort seq = m_ServerProcessed;
            m_ServerProcessed = (ushort)(m_ServerProcessed + 1);
            m_OwnerStateDirty = true;

            float fastForward = ServerFastForwardSeconds();
            if (!ServerValidateShot(ref rayOrigin, ref rayDirection))
            {
                ShotRejectedClientRpc(seq, (byte)RejectReason.Invalid, m_OwnerRpcParams);
                return;
            }
            if (!ServerCanShoot(true, fastForward))
            {
                ShotRejectedClientRpc(seq, (byte)RejectReason.CannotShoot, m_OwnerRpcParams);
                return;
            }

            ServerFireBolts(rayOrigin, rayDirection, seq, fastForward);
            if (shotSeq != seq) ShotRejectedClientRpc(seq, (byte)RejectReason.Resync, m_OwnerRpcParams);
        }

        /// <summary>
        /// Server: fire at a world position on behalf of this bot. Same rules as a player's shot.
        /// <paramref name="aimErrorDegrees"/> is the bot's own aim wobble, an angle so it scales with range.
        /// </summary>
        public void FireFromAI(Vector3 targetAimPosition, float aimErrorDegrees = 0f)
        {
            if (!IsServer) return;
            // Bots run on the server, so there is no jitter to tolerate: plain fire-rate gate.
            if (Time.time < m_ServerNextFireTime) return;
            if (!ServerCanShoot(false, 0f)) return;
            m_ServerNextFireTime = Time.time + fireRate;

            ushort seq = m_ServerProcessed;
            m_ServerProcessed = (ushort)(m_ServerProcessed + 1);
            Vector3 origin = AIFireOrigin;
            Vector3 aim = targetAimPosition - origin;
            if (aim.sqrMagnitude < 1e-6f) aim = transform.forward;
            ServerFireBolts(origin, ApplySpread(aim.normalized, aimErrorDegrees), seq, 0f);
        }

        // Half the shooter's round trip as the server measures it (never as the client claims),
        // capped. Bots and the host's own player are already on the server's clock.
        private float ServerFastForwardSeconds()
        {
            if (IsBot || NetworkManager == null || OwnerClientId == NetworkManager.ServerClientId) return 0f;
            ulong rtt = NetworkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(OwnerClientId);
            return Mathf.Min(rtt / 2000f, Settings.maxFastForwardSeconds);
        }

        // Owner: how old a snapshot from the server is.
        private float OneWaySeconds()
        {
            if (IsServer || NetworkManager == null) return 0f;
            return NetworkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId) / 2000f;
        }

        /// <summary>
        /// Server: alive, match running and not locked (with a little leeway for a human's shots
        /// already in flight when a lock started or ends), and ammo for a magazine weapon. An empty
        /// magazine starts a reload.
        /// </summary>
        private bool ServerCanShoot(bool human, float fastForward)
        {
            if (m_Health != null && m_Health.IsDead) return false;
            if (IsMatchOver) return false;

            SustainParams p = Params;
            double now = Time.timeAsDouble;
            if (m_ServerLock != WeaponLock.None)
            {
                // Shots the owner fired before it could know the server had overheated.
                bool overheatGrace = human && m_ServerLock == WeaponLock.Overheat &&
                                     now - m_ServerLockStart <= Mathf.Min(2f * fastForward, 2f * Settings.maxFastForwardSeconds) + fireTimingTolerance;
                if (!overheatGrace)
                {
                    if (m_ServerLockEnd - now > fireTimingTolerance) return false;
                    ServerFinishLock();
                }
            }

            if (!p.UsesHeat && m_ServerAmmo <= 0)
            {
                ServerStartLock(WeaponLock.Reload, p.ReloadSeconds);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Server: the one place a shot is fired, for players and bots alike. Spends a round or adds
        /// heat, launches every pellet as a bolt, then tells clients to draw it.
        /// </summary>
        private void ServerFireBolts(Vector3 origin, Vector3 direction, ushort seq, float fastForward)
        {
            if (m_Health != null) m_Health.EndSpawnProtection();
            ServerLastShotTime = Time.time;

            SustainParams p = Params;
            double now = Time.timeAsDouble;
            if (p.UsesHeat)
            {
                ConquestHeatModel.ApplyShot(ref m_ServerHeat, ref m_ServerCoolFrom, now, p);
                // A little below the limit, so the server never overheats after the owner's prediction.
                if (m_ServerLock == WeaponLock.None && m_ServerHeat >= p.ShotsToOverheat - Settings.heatToleranceShots)
                {
                    ServerStartLock(WeaponLock.Overheat, p.LockoutSeconds - fastForward);
                }
            }
            else
            {
                m_ServerAmmo = Mathf.Max(0, m_ServerAmmo - 1);
                if (m_ServerAmmo == 0) ServerStartLock(WeaponLock.Reload, p.ReloadSeconds - fastForward);
            }
            m_OwnerStateDirty = true;

            Team team = m_Player != null ? m_Player.CurrentTeam : Team.Neutral;
            uint seed = ConquestBallistics.Seed(NetworkObjectId, seq);
            for (int i = 0; i < bulletsPerShot; i++)
            {
                ConquestBoltSimulator.Launch(this, origin, ConquestBallistics.Pellet(direction, spreadAngle, seed, i), boltSpeed, maxRange,
                    boltRadius, damagePerShot, hitLayers, team, seq, (byte)i, fastForward);
            }

            BoltFiredClientRpc(origin, direction, seq, NetworkManager.ServerTime.Time - fastForward);
        }

        /// <summary>
        /// Never trust the client's shot: enforce the class fire rate (charged even for a bad
        /// request), reject malformed rays and origins far from where the server thinks this
        /// player's camera is, and start a bolt from behind a wall at the server's camera instead.
        /// </summary>
        private bool ServerValidateShot(ref Vector3 rayOrigin, ref Vector3 rayDirection)
        {
            // Rate limit that tolerates bunched-up packets but holds the long-run average to fireRate.
            float now = Time.time;
            if (now < m_ServerNextFireTime - fireTimingTolerance) return false;
            m_ServerNextFireTime = Mathf.Max(now, m_ServerNextFireTime) + fireRate;

            if (!IsFinite(rayOrigin) || !IsFinite(rayDirection) || rayDirection.sqrMagnitude < 1e-6f) return false;
            rayDirection.Normalize();

            // Movement is owner-authoritative, so the server's copy lags slightly; the tolerance covers that.
            Vector3 expectedOrigin = (m_Movement != null && m_Movement.PlayerCamera != null)
                ? m_Movement.PlayerCamera.transform.position
                : transform.position + Vector3.up * 1.6f;
            if ((rayOrigin - expectedOrigin).sqrMagnitude > maxRayOriginOffset * maxRayOriginOffset) return false;
            if (ConquestPhysics.LinecastIgnoring(transform, expectedOrigin, rayOrigin, ConquestPhysics.WorldMask, out _))
            {
                rayOrigin = expectedOrigin;
            }
            return true;
        }

        private static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                     float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }

        /// <summary>Server: a bolt from this weapon hit an enemy. Sent to the shooter once per frame at most.</summary>
        internal void ServerNoteHit(bool killed)
        {
            if (IsBot || !IsSpawned) return;
            m_PendingHit = true;
            m_PendingKill |= killed;
        }

        [ClientRpc]
        private void BoltFiredClientRpc(Vector3 origin, Vector3 direction, ushort shotSeq, double fireServerTime)
        {
            // The human shooter already drew this shot. Bots are owned by the host, so the
            // host still needs to see and hear their fire.
            if (PredictsLocally) return;

            Vector3 muzzle = muzzlePoint != null ? muzzlePoint.position : origin;
            SpawnShotVisuals(origin, direction, muzzle, shotSeq, false, fireServerTime);
            PlayMuzzleFlash();
            PlayFireAudio();
            m_Animator?.TriggerFire();
        }

        [ClientRpc]
        private void ShotRejectedClientRpc(ushort shotSeq, byte reason, ClientRpcParams clientRpcParams = default)
        {
            if (!PredictsLocally) return;
            if (reason == (byte)RejectReason.Resync)
            {
                // The server numbers shots differently from here: continue from its count.
                m_Predictor.ResyncSeq((ushort)(shotSeq + 1));
                return;
            }
            ConquestBoltPool.EndShot(NetworkObjectId, shotSeq);
        }

        [ClientRpc]
        private void ConfirmHitClientRpc(bool killed, ClientRpcParams clientRpcParams = default)
        {
            ConquestHUD.TriggerHitmarker(killed);
            AudioClip clip = killed && killConfirmClip != null ? killConfirmClip : targetHitClip;
            if (weaponAudioSource != null && clip != null)
            {
                // Play subtle hit confirmation using the audio limiter
                ConquestAudioLimiter.TryPlayHitFeedback(weaponAudioSource, clip, killed ? 0.3f : 0.16f);
            }
        }

        private void HandleOwnerStateChanged(WeaponOwnerState previous, WeaponOwnerState current)
        {
            if (PredictsLocally) m_Predictor.OnSnapshot(Params, current, Time.timeAsDouble, OneWaySeconds());
        }

        private void TryStartReload(in SustainParams p, double now)
        {
            if (!m_Predictor.OnReloadRequested(p, now)) return;

            // Animation and sound follow the replicated isReloading flag, on every peer.
            if (IsServer) ServerStartReload();
            else RequestReloadServerRpc();
        }

        [ServerRpc]
        private void RequestReloadServerRpc()
        {
            if (m_Health != null && m_Health.IsDead) return;
            if (IsMatchOver) return;
            ServerStartReload();
        }

        /// <summary>Server: start a reload unless one is running, the magazine is full or the weapon vents heat instead.</summary>
        private void ServerStartReload()
        {
            if (UsesHeat || m_ServerLock != WeaponLock.None || m_ServerAmmo >= magazineCapacity) return;
            ServerStartLock(WeaponLock.Reload, reloadDuration - ServerFastForwardSeconds());
        }

        private void ServerStartLock(WeaponLock weaponLock, float seconds)
        {
            double now = Time.timeAsDouble;
            m_ServerLock = weaponLock;
            m_ServerLockStart = now;
            m_ServerLockEnd = now + Mathf.Max(0f, seconds);
            m_ServerLockCount = (byte)((m_ServerLockCount + 1) & 0x03);
            if (IsSpawned)
            {
                if (weaponLock == WeaponLock.Reload) isReloading.Value = true;
                if (weaponLock == WeaponLock.Overheat) isOverheated.Value = true;
            }
            m_OwnerStateDirty = true;
        }

        // Polled instead of a coroutine: no allocation, and nothing to stop on despawn.
        private void ServerTickLock()
        {
            if (m_ServerLock != WeaponLock.None && Time.timeAsDouble >= m_ServerLockEnd) ServerFinishLock();
        }

        private void ServerFinishLock()
        {
            SustainParams p = Params;
            if (m_ServerLock == WeaponLock.Reload)
            {
                m_ServerAmmo = magazineCapacity;
            }
            else if (m_ServerLock == WeaponLock.Overheat)
            {
                m_ServerHeat = p.HeatAfter * p.ShotsToOverheat;
                m_ServerCoolFrom = Time.timeAsDouble;
            }
            m_ServerLock = WeaponLock.None;
            ServerClearLockFlags();
            m_OwnerStateDirty = true;
        }

        private void ServerClearLockFlags()
        {
            if (!IsSpawned) return;
            if (isReloading.Value) isReloading.Value = false;
            if (isOverheated.Value) isOverheated.Value = false;
        }

        /// <summary>
        /// Server: drop any lock, refill the magazine and cool the weapon. Called on death,
        /// respawn, class change, team change and spawn, so a reset player never keeps a ghost
        /// reload or a hot gun. The owner adopts it whole (a new epoch).
        /// </summary>
        public void ServerResetAmmo()
        {
            if (!IsServer) return;
            m_ServerLock = WeaponLock.None;
            m_ServerAmmo = magazineCapacity;
            m_ServerHeat = 0f;
            m_ServerCoolFrom = 0.0;
            m_ServerEpoch = (byte)((m_ServerEpoch + 1) & 0x0F);
            ServerClearLockFlags();
            m_OwnerStateDirty = true;
        }

        private void ServerWriteOwnerState()
        {
            m_OwnerStateDirty = false;
            // The host owns every bot, so their state would go nowhere.
            if (IsBot) return;
            double now = Time.timeAsDouble;
            float heat = ConquestHeatModel.HeatAt(m_ServerHeat, m_ServerCoolFrom, now, Params.CoolPerSecond);
            double lockLeft = m_ServerLock != WeaponLock.None ? m_ServerLockEnd - now : 0.0;
            ownerState.Value = WeaponOwnerState.Pack(m_ServerProcessed, heat, lockLeft, m_ServerAmmo, m_ServerLock,
                m_ServerLockCount, m_ServerEpoch);
        }

        // Runs on every peer (the server included) once per reload, on the false-to-true edge.
        private void HandleReloadingChanged(bool wasReloading, bool reloading)
        {
            if (!reloading || wasReloading) return;
            if (m_Health != null && m_Health.IsDead) return;

            m_Animator?.TriggerReload();
            if (weaponAudioSource != null && reloadSoundClip != null)
            {
                weaponAudioSource.pitch = 1.0f;
                weaponAudioSource.PlayOneShot(reloadSoundClip, 0.65f);
            }
        }

        // Every peer hears a weapon overheat and come back.
        private void HandleOverheatedChanged(bool wasOverheated, bool overheated)
        {
            if (overheated == wasOverheated || (m_Health != null && m_Health.IsDead)) return;
            ConquestFactionData faction = Faction;
            AudioClip clip = faction != null ? (overheated ? faction.overheatClip : faction.ventDoneClip) : null;
            if (weaponAudioSource != null && clip != null)
            {
                weaponAudioSource.pitch = 1.0f;
                weaponAudioSource.PlayOneShot(clip, 0.65f);
            }
        }

        /// <summary>
        /// Server: a support station pulse. A magazine weapon gets a full magazine now (cutting a
        /// reload short); a heat weapon vents the faction's share of a full gauge.
        /// </summary>
        public void ResupplyAmmo()
        {
            if (!IsServer) return;
            if (!UsesHeat)
            {
                if (m_ServerLock == WeaponLock.Reload || m_ServerAmmo < magazineCapacity) ServerResetAmmo();
                return;
            }

            SustainParams p = Params;
            double now = Time.timeAsDouble;
            float heat = ConquestHeatModel.HeatAt(m_ServerHeat, m_ServerCoolFrom, now, p.CoolPerSecond);
            bool overheated = m_ServerLock == WeaponLock.Overheat;
            if (heat <= 0f && !overheated) return;

            ConquestFactionData faction = Faction;
            float vent = faction != null ? faction.stationHeatVent : 1f;
            m_ServerHeat = Mathf.Max(0f, heat - vent * p.ShotsToOverheat);
            m_ServerCoolFrom = now;
            if (overheated && vent >= 1f)
            {
                m_ServerLock = WeaponLock.None;
                ServerClearLockFlags();
            }
            m_ServerEpoch = (byte)((m_ServerEpoch + 1) & 0x0F);
            m_OwnerStateDirty = true;
        }

        /// <summary>Owner: seconds until the station can be deployed again (as last confirmed by the server).</summary>
        public float StationCooldownRemaining => Mathf.Max(0f, m_NextStationDeployTime - Time.time);

        /// <summary>Owner: why the last deploy was refused, shown briefly on the HUD; empty otherwise.</summary>
        public string StationRefusal => Time.time < m_StationRefusalUntil ? m_StationRefusal : "";

        private string m_StationRefusal = "";
        private float m_StationRefusalUntil;

        private void TryDeployStation()
        {
            // The cooldown only starts when the server confirms a deploy (StationDeployResultClientRpc).
            if (Time.time < m_NextStationDeployTime) return;
            DeployStationServerRpc();
        }

        [ServerRpc]
        private void DeployStationServerRpc()
        {
            StationDeployResult result = ServerTryDeployStation();
            if (result != StationDeployResult.Ignored)
            {
                StationDeployResultClientRpc(result, Mathf.Max(0f, m_ServerNextStationTime - Time.time), m_OwnerRpcParams);
            }
        }

        public enum StationDeployResult : byte { Ignored, Deployed, CoolingDown, TeamLimit }

        /// <summary>Server: true if this player's own station is up and covers <paramref name="position"/>.</summary>
        public bool ServerOwnStationCovers(Vector3 position)
        {
            if (m_ServerActiveStation == null || !m_ServerActiveStation.IsSpawned) return false;
            var station = m_ServerActiveStation.GetComponent<ConquestSupportStation>();
            float range = station != null ? station.Radius : 0f;
            return (m_ServerActiveStation.transform.position - position).sqrMagnitude <= range * range;
        }

        /// <summary>
        /// Server: deploy this player's (or bot's) support station in front of them. Capability,
        /// alive state, cooldown and the per-team cap are all checked here, never on the client.
        /// </summary>
        public StationDeployResult ServerTryDeployStation()
        {
            if (!IsServer || supportStationPrefab == null || m_Player == null) return StationDeployResult.Ignored;
            if (!hasSupportStation || IsMatchOver) return StationDeployResult.Ignored;
            if (m_Health != null && m_Health.IsDead) return StationDeployResult.Ignored;
            if (Time.time < m_ServerNextStationTime) return StationDeployResult.CoolingDown;

            // One active station per player: deploying again replaces the old one, so it never
            // counts against the team cap.
            bool replacing = m_ServerActiveStation != null && m_ServerActiveStation.IsSpawned;
            if (!replacing && ConquestSupportStation.CountForTeam(m_Player.CurrentTeam) >= maxStationsPerTeam)
            {
                return StationDeployResult.TeamLimit;
            }

            if (replacing) m_ServerActiveStation.Despawn(true);
            m_ServerActiveStation = null;

            if (m_Health != null) m_Health.EndSpawnProtection();
            m_ServerNextStationTime = Time.time + stationCooldown;

            // Position comes from the server's view of the player, never from the client.
            Vector3 position = transform.position + transform.forward * stationDeployDistance + Vector3.up * 0.1f;
            GameObject stationObj = Instantiate(supportStationPrefab, position, transform.rotation);
            var netObj = stationObj.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Destroy(stationObj);
                return StationDeployResult.Ignored;
            }

            // Team first, so the station is counted for the right side from its first frame.
            var stationComp = stationObj.GetComponent<ConquestSupportStation>();
            if (stationComp != null) stationComp.stationTeam.Value = m_Player.CurrentTeam;
            netObj.Spawn();
            m_ServerActiveStation = netObj;
            return StationDeployResult.Deployed;
        }

        [ClientRpc]
        private void StationDeployResultClientRpc(StationDeployResult result, float cooldownLeft, ClientRpcParams rpcParams = default)
        {
            if (result == StationDeployResult.Deployed || result == StationDeployResult.CoolingDown)
            {
                m_NextStationDeployTime = Time.time + cooldownLeft;
            }
            if (result == StationDeployResult.TeamLimit)
            {
                m_StationRefusal = "TEAM STATION LIMIT";
                m_StationRefusalUntil = Time.time + 2f;
            }
        }

        private void PlayMuzzleFlash()
        {
            if (muzzlePoint == null) return;
            ConquestFxPool.Play(muzzleFlashPrefab, muzzlePoint.position, muzzlePoint.rotation, muzzleFlashSeconds);
        }

        private void PlayFireAudio()
        {
            if (weaponAudioSource != null && fireSoundClip != null)
            {
                // Concurrency-limited fire sound with category cooldown
                ConquestAudioLimiter.TryPlayOneShot(weaponAudioSource, fireSoundClip, "Gunshot", fireVolume, 0.04f, firePitchMin, firePitchMax);
            }
        }
    }
}
