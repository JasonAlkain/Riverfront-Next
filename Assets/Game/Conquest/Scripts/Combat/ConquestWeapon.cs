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
    /// A class also carries a fixed sidearm. Switching swaps this component's stats in place;
    /// the server keeps the holstered weapon's ammo and heat so switching back doesn't refill it.
    /// </summary>
    public class ConquestWeapon : NetworkBehaviour
    {
        public enum WeaponSlot : byte { Primary = 0, Sidearm = 1 }

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

        [Header("Weapon Switch")]
        [Tooltip("Placeholder: seconds to lower the weapon in hand. Nothing fires until the drawn weapon is raised.")]
        [SerializeField] private float lowerSeconds = 0.2f;
        [Tooltip("Placeholder: seconds to raise the drawn weapon.")]
        [SerializeField] private float raiseSeconds = 0.25f;
        [Tooltip("Played on every peer when a weapon is put away and when one is drawn.")]
        [SerializeField] private AudioClip switchClip;
        [SerializeField] private float switchVolume = 0.5f;
        [Tooltip("Where a sidearm's shots and muzzle flash start. Empty uses the primary's Muzzle Point.")]
        [SerializeField] private Transform sidearmMuzzlePoint;

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

        /// <summary>The weapon in hand (a WeaponSlot). Every peer applies its stats, so remote shots draw right.</summary>
        public NetworkVariable<byte> activeSlot = new NetworkVariable<byte>(
            (byte)WeaponSlot.Primary,
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

        private ConquestClassData m_ClassData;
        private AudioClip m_DefaultFireClip;
        private AudioClip m_DefaultReloadClip;
        private string m_WeaponName = "";
        private Sprite m_WeaponIcon;
        private int m_RepairPerUse;

        // Owner: lower the weapon in hand, wait for the server's switch, raise the drawn one.
        private enum SwitchPhase : byte { None, Lowering, Waiting, Raising }
        private SwitchPhase m_SwitchPhase;
        private float m_SwitchPhaseStart;
        private byte m_ShownSlot = (byte)WeaponSlot.Primary;
        // Owner: the slot asked for and not yet confirmed by the server (NoPendingSlot when none).
        private const byte NoPendingSlot = 255;
        private byte m_PendingSlot = NoPendingSlot;
        private float m_PendingSlotUntil;
        // Server: the holstered weapon's state. Invalid means full (never fired, or just resupplied).
        private bool m_StashValid;
        private int m_StashAmmo;
        private float m_StashHeat;
        private double m_StashCoolFrom;
        private float m_StashOverheatLeft;

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
        public WeaponSlot ActiveSlot => (WeaponSlot)activeSlot.Value;
        public bool HasSidearm => m_ClassData != null && m_ClassData.sidearm != null && m_ClassData.sidearm.kind != SidearmKind.None;
        /// <summary>The weapon in hand fires no bolts: it repairs supply droids.</summary>
        public bool IsRepairTool => ActiveSlot == WeaponSlot.Sidearm && HasSidearm && m_ClassData.sidearm.kind == SidearmKind.RepairTool;
        public SidearmKind SidearmKind => HasSidearm ? m_ClassData.sidearm.kind : SidearmKind.None;
        /// <summary>Name of the weapon in hand, for the HUD and the kill feed.</summary>
        public string ActiveWeaponName => m_WeaponName;
        /// <summary>Icon of the weapon in hand, for the HUD. May be null.</summary>
        public Sprite ActiveWeaponIcon => m_WeaponIcon;
        /// <summary>
        /// The weapon whose model is up. On the switching player's own screen it changes when the old
        /// weapon is fully lowered; everyone else sees it change with the server's switch.
        /// </summary>
        public WeaponSlot ShownSlot => PredictsLocally ? (WeaponSlot)m_ShownSlot : ActiveSlot;
        /// <summary>Owner: a switch is under way (lowering, waiting for the server, or raising). Nothing fires.</summary>
        public bool IsSwitching => m_SwitchPhase != SwitchPhase.None;
        /// <summary>Owner: how far the viewmodel is lowered, 0 (ready) to 1 (out of sight).</summary>
        public float Lowered01
        {
            get
            {
                float elapsed = Time.time - m_SwitchPhaseStart;
                switch (m_SwitchPhase)
                {
                    case SwitchPhase.Lowering: return lowerSeconds > 0f ? Mathf.Clamp01(elapsed / lowerSeconds) : 1f;
                    case SwitchPhase.Waiting: return 1f;
                    case SwitchPhase.Raising: return raiseSeconds > 0f ? 1f - Mathf.Clamp01(elapsed / raiseSeconds) : 0f;
                    default: return 0f;
                }
            }
        }

        // The server blocks a drawn weapon for the whole animation, so a quick client can't fire early.
        private float SwitchSeconds => Mathf.Max(0f, lowerSeconds) + Mathf.Max(0f, raiseSeconds);
        private Transform ActiveMuzzle => ActiveSlot == WeaponSlot.Sidearm && sidearmMuzzlePoint != null ? sidearmMuzzlePoint : muzzlePoint;
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
            m_DefaultFireClip = fireSoundClip;
            m_DefaultReloadClip = reloadSoundClip;
        }

        public override void OnNetworkDespawn()
        {
            isReloading.OnValueChanged -= HandleReloadingChanged;
            isOverheated.OnValueChanged -= HandleOverheatedChanged;
            ownerState.OnValueChanged -= HandleOwnerStateChanged;
            activeSlot.OnValueChanged -= HandleSlotChanged;
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
            activeSlot.OnValueChanged += HandleSlotChanged;
            // A late joiner may see someone holding their sidearm already.
            if (m_ClassData != null) ApplySlotStats(ActiveSlot);

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

            m_ClassData = data;
            hasSupportStation = data.hasSupportStation;
            m_PendingSlot = NoPendingSlot;
            m_SwitchPhase = SwitchPhase.None;
            m_ShownSlot = (byte)WeaponSlot.Primary;
            // A new class, or a respawn: back to the primary. The slot handler applies its stats.
            if (IsServer && IsSpawned && activeSlot.Value != (byte)WeaponSlot.Primary) activeSlot.Value = (byte)WeaponSlot.Primary;
            ApplySlotStats(IsSpawned ? ActiveSlot : WeaponSlot.Primary);

            // Also runs on every respawn (ConquestPlayer.HandleRespawn), which is what refills the
            // magazine and cools the weapon.
            ServerResetAmmo();
            if (PredictsLocally) m_Predictor.Reset(Params);
        }

        // Copies the primary's or the sidearm's stats into the fields every shot reads.
        private void ApplySlotStats(WeaponSlot slot)
        {
            ConquestClassData data = m_ClassData;
            if (data == null) return;

            ConquestSidearmData side = data.sidearm;
            if (slot == WeaponSlot.Sidearm && side != null && side.kind != SidearmKind.None)
            {
                m_WeaponName = side.weaponName;
                m_WeaponIcon = side.icon;
                damagePerShot = side.damagePerShot;
                fireRate = side.fireRate;
                maxRange = side.maxRange;
                bulletsPerShot = 1;
                spreadAngle = side.spreadAngle;
                magazineCapacity = Mathf.Clamp(side.magazineCapacity, 1, 255);
                reloadDuration = side.reloadDuration;
                isAutomatic = side.isAutomatic;
                boltSpeed = Mathf.Max(0.1f, side.boltSpeed);
                boltRadius = 0f;
                shotsToOverheat = Mathf.Clamp(side.shotsToOverheat, 1, 255);
                coolDelaySeconds = side.coolDelaySeconds;
                fullCoolSeconds = side.fullCoolSeconds;
                overheatLockoutSeconds = side.overheatLockoutSeconds;
                heatAfterOverheat = 0f;
                hasZoom = false;
                hasScope = false;
                m_RepairPerUse = side.repairPerUse;
                // A sidearm only ever plays its own sounds; an unassigned one stays silent rather
                // than borrowing the primary's.
                fireSoundClip = side.fireSound;
                reloadSoundClip = side.reloadSound;
            }
            else
            {
                m_WeaponName = data.weaponName;
                m_WeaponIcon = data.weaponIcon;
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
                m_RepairPerUse = 0;
                fireSoundClip = data.fireSound != null ? data.fireSound : m_DefaultFireClip;
                reloadSoundClip = data.reloadSound != null ? data.reloadSound : m_DefaultReloadClip;
            }

            firePitchMin = data.firePitchMin;
            firePitchMax = data.firePitchMax;
            fireVolume = data.fireVolume;
        }

        // Every peer: the server switched weapons. The owner restarts its prediction from the
        // server's state (the switch bumps the epoch, so the next snapshot is adopted whole).
        private void HandleSlotChanged(byte previous, byte current)
        {
            ApplySlotStats((WeaponSlot)current);
            if (m_PendingSlot == current) m_PendingSlot = NoPendingSlot;
            if (!PredictsLocally)
            {
                // The switching player hears their own holster and draw from the animation.
                PlaySwitchAudio();
                return;
            }
            m_Predictor.Reset(Params);
            m_Predictor.OnSnapshot(Params, ownerState.Value, Time.timeAsDouble, OneWaySeconds());
        }

        private void RequestSwitch()
        {
            byte target = activeSlot.Value == (byte)WeaponSlot.Primary ? (byte)WeaponSlot.Sidearm : (byte)WeaponSlot.Primary;
            m_PendingSlot = target;
            m_PendingSlotUntil = Time.time + 2f;
            m_SwitchPhase = SwitchPhase.Lowering;
            m_SwitchPhaseStart = Time.time;
            m_IsZoomed = false;
            PlaySwitchAudio();
            SwitchWeaponServerRpc(target);
        }

        // Owner, every frame: lowered, then (once the server has switched, or the request timed
        // out) the drawn weapon comes up. The fire time is pushed to the end of the raise.
        private void TickSwitch()
        {
            if (m_SwitchPhase == SwitchPhase.None) return;
            if (m_PendingSlot != NoPendingSlot && Time.time > m_PendingSlotUntil) m_PendingSlot = NoPendingSlot;

            if (m_SwitchPhase == SwitchPhase.Lowering && Time.time - m_SwitchPhaseStart >= lowerSeconds)
            {
                m_SwitchPhase = SwitchPhase.Waiting;
            }
            if (m_SwitchPhase == SwitchPhase.Waiting && m_PendingSlot == NoPendingSlot)
            {
                m_ShownSlot = activeSlot.Value;
                m_SwitchPhase = SwitchPhase.Raising;
                m_SwitchPhaseStart = Time.time;
                m_NextFireTime = Mathf.Max(m_NextFireTime, Time.time + raiseSeconds);
                PlaySwitchAudio();
            }
            if (m_SwitchPhase == SwitchPhase.Raising && Time.time - m_SwitchPhaseStart >= raiseSeconds)
            {
                m_SwitchPhase = SwitchPhase.None;
            }
        }

        private void PlaySwitchAudio()
        {
            if (weaponAudioSource != null && switchClip != null && weaponAudioSource.isActiveAndEnabled)
            {
                weaponAudioSource.PlayOneShot(switchClip, switchVolume);
            }
        }

        [ServerRpc]
        private void SwitchWeaponServerRpc(byte slot)
        {
            ServerSwitchTo((WeaponSlot)slot);
        }

        /// <summary>
        /// Server: holster the weapon in hand and draw the other. A reload in progress is dropped;
        /// the holstered weapon keeps its rounds and heat, and keeps cooling. An overheat lock is
        /// kept too, so switching away and back can't skip it.
        /// </summary>
        private void ServerSwitchTo(WeaponSlot slot)
        {
            if (m_Health != null && m_Health.IsDead) return;
            if (IsMatchOver || (byte)slot == activeSlot.Value) return;
            if (slot != WeaponSlot.Primary && (slot != WeaponSlot.Sidearm || !HasSidearm)) return;

            double now = Time.timeAsDouble;
            float heldHeat = ConquestHeatModel.HeatAt(m_ServerHeat, m_ServerCoolFrom, now, Params.CoolPerSecond);
            int heldAmmo = m_ServerAmmo;
            bool drawnValid = m_StashValid;
            int drawnAmmo = m_StashAmmo;
            float drawnHeat = m_StashHeat;
            double drawnCoolFrom = m_StashCoolFrom;
            float drawnOverheatLeft = m_StashOverheatLeft;

            m_StashValid = true;
            m_StashAmmo = heldAmmo;
            m_StashHeat = heldHeat;
            m_StashCoolFrom = now;
            m_StashOverheatLeft = m_ServerLock == WeaponLock.Overheat ? Mathf.Max(0f, (float)(m_ServerLockEnd - now)) : 0f;

            m_ServerLock = WeaponLock.None;
            ServerClearLockFlags();
            activeSlot.Value = (byte)slot; // the handler applies the drawn weapon's stats here on the server too
            m_ServerAmmo = drawnValid ? Mathf.Min(drawnAmmo, magazineCapacity) : magazineCapacity;
            m_ServerHeat = drawnValid ? drawnHeat : 0f;
            m_ServerCoolFrom = drawnValid ? drawnCoolFrom : 0.0;
            // The lock kept running while holstered: only what is left of it still applies.
            float overheatLeft = drawnValid ? drawnOverheatLeft - (float)(now - drawnCoolFrom) : 0f;
            if (overheatLeft > 0f) ServerStartLock(WeaponLock.Overheat, overheatLeft);
            m_ServerEpoch = (byte)((m_ServerEpoch + 1) & 0x0F);
            m_ServerNextFireTime = Mathf.Max(m_ServerNextFireTime, Time.time + SwitchSeconds);
            m_OwnerStateDirty = true;
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
            TickSwitch();
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

            if (HasSidearm && !IsSwitching && ConquestInput.SwitchWeaponPressed) RequestSwitch();

            // Nothing fires from the moment a switch starts until the drawn weapon is up.
            bool switching = IsSwitching;
            if (IsRepairTool)
            {
                if (!switching && fireRequested && Time.time >= m_NextFireTime) TryRepairLocal();
            }
            else if (!switching && fireRequested && Time.time >= m_NextFireTime)
            {
                if (m_Predictor.CanFire(p, now)) TryFireLocal(p, now);
                // An empty magazine the predictor didn't lock itself (adopted from the server): reload.
                else if (!p.UsesHeat && m_Predictor.Lock == WeaponLock.None && m_Predictor.Ammo <= 0) TryStartReload(p, now);
            }

            // Heat weapons never reload; they vent by themselves.
            if (!p.UsesHeat && !IsRepairTool && !switching && ConquestInput.ReloadPressed)
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

            m_IsZoomed = canAct && hasZoom && !IsSwitching && ConquestInput.AimHeld;

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
            Transform activeMuzzle = ActiveMuzzle;
            Vector3 muzzle = activeMuzzle != null ? activeMuzzle.position : baseRay.origin;
            SpawnShotVisuals(baseRay.origin, baseRay.direction, muzzle, seq, true, now);

            PlayMuzzleFlash();
            PlayFireAudio();
            m_Animator?.TriggerFire();

            FireServerRpc(baseRay.origin, baseRay.direction, seq);
        }

        // The repair tool fires no bolts and spends no ammo, so it skips the prediction entirely.
        private void TryRepairLocal()
        {
            m_NextFireTime = Time.time + fireRate;
            Camera cam = m_Movement.PlayerCamera;
            if (cam == null) return;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            RepairServerRpc(ray.origin, ray.direction);
        }

        [ServerRpc]
        private void RepairServerRpc(Vector3 rayOrigin, Vector3 rayDirection)
        {
            if (!IsRepairTool || (m_Health != null && m_Health.IsDead) || IsMatchOver) return;
            if (!ServerValidateShot(ref rayOrigin, ref rayDirection)) return;
            if (!ConquestPhysics.RaycastIgnoring(transform, rayOrigin, rayDirection, maxRange, hitLayers, out RaycastHit hit)) return;

            ConquestSupplyDroid droid = hit.collider.GetComponentInParent<ConquestSupplyDroid>();
            Team team = m_Player != null ? m_Player.CurrentTeam : Team.Neutral;
            if (droid != null && droid.Serves(team)) droid.ServerRepair(m_RepairPerUse);
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

            Transform activeMuzzle = ActiveMuzzle;
            Vector3 muzzle = activeMuzzle != null ? activeMuzzle.position : origin;
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
            m_StashValid = false;
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
        /// reload short); a heat weapon vents the faction's share of a full gauge. The holstered
        /// weapon is refilled too.
        /// </summary>
        public void ResupplyAmmo()
        {
            if (!IsServer) return;
            m_StashValid = false;
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
            Transform muzzle = ActiveMuzzle;
            if (muzzle == null) return;
            ConquestFxPool.Play(muzzleFlashPrefab, muzzle.position, muzzle.rotation, muzzleFlashSeconds);
        }

        // Placeholder: gunshots from other players that may start in one 40 ms window.
        private const int GunshotVoices = 4;

        private void PlayFireAudio()
        {
            if (weaponAudioSource != null && fireSoundClip != null)
            {
                // Your own shots always play; everyone else's share a few voices per 40 ms, so a
                // firefight stays readable without dropping a shot just because someone else fired.
                ConquestAudioLimiter.TryPlayOneShot(weaponAudioSource, fireSoundClip, "Gunshot", fireVolume, 0.04f, firePitchMin, firePitchMax,
                    GunshotVoices, PredictsLocally);
            }
        }
    }
}
