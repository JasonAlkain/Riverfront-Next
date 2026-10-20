using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Game.Conquest.Player;
using Game.Conquest.Classes;
using Game.Conquest.Gameplay;
using Game.Conquest.UI;
using Game.Conquest.Audio;

namespace Game.Conquest.Combat
{
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

        [Header("Special Abilities")]
        [SerializeField] private bool hasZoom = false;
        [SerializeField] private float zoomFOV = 35.0f;
        [SerializeField] private bool hasSupportStation = false;
        [SerializeField] private GameObject supportStationPrefab;
        [SerializeField] private float stationCooldown = 15.0f;
        [Tooltip("Placeholder: most stations one team may have out at once (each player still has at most one).")]
        [SerializeField] private int maxStationsPerTeam = 3;

        [Header("Server Validation")]
        [Tooltip("Shots may arrive this much earlier than the fire rate allows (network jitter) without being dropped.")]
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
        [Tooltip("Shared tracer materials per team. Assets, never instanced at runtime.")]
        [SerializeField] private Material redTracerMaterial;
        [SerializeField] private Material blueTracerMaterial;
        [SerializeField] private float tracerWidth = 0.05f;
        [SerializeField] private float tracerDuration = 0.06f;
        [SerializeField] private AudioSource weaponAudioSource;
        [SerializeField] private AudioClip fireSoundClip;
        [SerializeField] private AudioClip reloadSoundClip;
        [SerializeField] private AudioClip targetHitClip;
        [Tooltip("Optional: played instead of the hit sound when the shot killed. Empty uses the hit sound.")]
        [SerializeField] private AudioClip killConfirmClip;
        [SerializeField] private float firePitchMin = 0.95f;
        [SerializeField] private float firePitchMax = 1.05f;
        [SerializeField] private float fireVolume = 0.40f;

        // Synchronized Ammo
        public NetworkVariable<int> currentAmmo = new NetworkVariable<int>(
            32,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> isReloading = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private ConquestPlayerMovement m_Movement;
        private ConquestHealth m_Health;
        private ConquestPlayer m_Player;
        private ConquestPlayerAnimator m_Animator;
        private float m_NextFireTime;
        private float m_NextStationDeployTime;

        // Server-side state. Kept separate from the owner's prediction fields because on a host
        // the same component is both owner and server.
        private float m_ServerNextFireTime;
        private float m_ServerNextStationTime;
        private NetworkObject m_ServerActiveStation;
        private Coroutine m_ServerReload;
        private ClientRpcParams m_OwnerRpcParams;
        private bool m_IsZoomed = false;

        public int CurrentAmmo => currentAmmo.Value;
        public int MagazineCapacity => magazineCapacity;
        public bool IsReloading => isReloading.Value;
        public bool HasZoom => hasZoom;
        public bool HasSupportStation => hasSupportStation;

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

            if (m_ServerReload != null)
            {
                StopCoroutine(m_ServerReload);
                m_ServerReload = null;
            }
        }

        public override void OnNetworkSpawn()
        {
            isReloading.OnValueChanged += HandleReloadingChanged;

            if (IsServer)
            {
                // Cached so a hit confirmation doesn't allocate a target list every shot.
                m_OwnerRpcParams = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } } };
                if (m_Health != null) m_Health.OnDied += HandleServerDied;
                ServerResetAmmo();
            }
        }

        public void ApplyClass(ConquestClassData data)
        {
            if (data == null) return;

            damagePerShot = data.damagePerShot;
            fireRate = data.fireRate;
            maxRange = data.maxRange;
            bulletsPerShot = Mathf.Max(1, data.bulletsPerShot);
            spreadAngle = data.spreadAngle;
            magazineCapacity = data.magazineCapacity;
            reloadDuration = data.reloadDuration;
            isAutomatic = data.isAutomatic;
            hasZoom = data.hasZoom;
            zoomFOV = data.zoomFOV;
            hasSupportStation = data.hasSupportStation;

            if (data.fireSound != null) fireSoundClip = data.fireSound;
            if (data.reloadSound != null) reloadSoundClip = data.reloadSound;
            firePitchMin = data.firePitchMin;
            firePitchMax = data.firePitchMax;
            fireVolume = data.fireVolume;

            // Also runs on every respawn (ConquestPlayer.HandleRespawn), which is what refills the magazine.
            ServerResetAmmo();
        }

        private void HandleServerDied(ulong killerId)
        {
            ServerResetAmmo();
        }

        private void Update()
        {
            // The host owns every bot, so IsOwner alone would let the host's keys drive bot weapons.
            if (!PredictsLocally) return;
            bool canAct = !(m_Health != null && m_Health.IsDead) && !IsMatchOver &&
                          !(ConquestPauseMenu.Instance != null && ConquestPauseMenu.Instance.IsPaused);

            // Always runs, so dying or switching off a scoped class zooms back out.
            HandleZoom(canAct);
            if (canAct) HandleInput();
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

            // Firing
            bool fireRequested = isAutomatic ? (Mouse.current != null && Mouse.current.leftButton.isPressed)
                                             : (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);

            if (fireRequested && Time.time >= m_NextFireTime && !isReloading.Value)
            {
                if (currentAmmo.Value > 0)
                {
                    TryFireLocal();
                }
                else
                {
                    TryStartReload();
                }
            }

            // Reload key (R)
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame && !isReloading.Value && currentAmmo.Value < magazineCapacity)
            {
                TryStartReload();
            }

            // Deploy Support Station (Q)
            if (hasSupportStation && Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame)
            {
                TryDeployStation();
            }
        }

        private void HandleZoom(bool canAct)
        {
            if (m_Movement == null || m_Movement.PlayerCamera == null) return;

            m_IsZoomed = canAct && hasZoom && Mouse.current != null && Mouse.current.rightButton.isPressed;

            // The unzoomed FOV follows the settings, even when they change mid-match.
            float targetFOV = m_IsZoomed ? zoomFOV : m_Movement.BaseFieldOfView;
            m_Movement.PlayerCamera.fieldOfView = Mathf.Lerp(m_Movement.PlayerCamera.fieldOfView, targetFOV, Time.deltaTime * 12.0f);
        }

        private void TryFireLocal()
        {
            m_NextFireTime = Time.time + fireRate;

            Camera cam = m_Movement.PlayerCamera;
            if (cam == null) return;

            Vector3 origin = muzzlePoint != null ? muzzlePoint.position : cam.transform.position;
            Ray baseRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            // Predicted visuals for every pellet; the server's own trace decides the damage.
            PlayMuzzleFlash();
            for (int i = 0; i < bulletsPerShot; i++)
            {
                Vector3 shootDir = ApplySpread(baseRay.direction, spreadAngle);
                Vector3 targetPoint = origin + shootDir * maxRange;

                if (ConquestPhysics.RaycastIgnoring(transform, origin, shootDir, maxRange, hitLayers, out RaycastHit hit))
                {
                    targetPoint = hit.point;
                    PlayImpact(hit.point, hit.normal, hit.collider.GetComponentInParent<ConquestHealth>() != null);
                }
                SpawnLocalTracer(origin, targetPoint);
            }

            // Play fire audio ONCE per trigger
            PlayFireAudio();
            m_Animator?.TriggerFire();

            // Authoritative server fire
            FireServerRpc(baseRay.origin, baseRay.direction);
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
        private void FireServerRpc(Vector3 rayOrigin, Vector3 rayDirection)
        {
            if (!ServerCanShoot()) return;
            if (!ServerValidateShot(ref rayOrigin, ref rayDirection)) return;

            ServerResolveShot(rayOrigin, rayDirection, muzzlePoint != null ? muzzlePoint.position : rayOrigin);
        }

        /// <summary>
        /// Server: fire at a world position on behalf of this bot. Same rules as a player's shot.
        /// <paramref name="aimErrorDegrees"/> is the bot's own aim wobble, an angle so it scales with range.
        /// </summary>
        public void FireFromAI(Vector3 targetAimPosition, float aimErrorDegrees = 0f)
        {
            if (!IsServer || !ServerCanShoot()) return;

            // Bots run on the server, so there is no jitter to tolerate: plain fire-rate gate.
            if (Time.time < m_ServerNextFireTime) return;
            m_ServerNextFireTime = Time.time + fireRate;

            Vector3 origin = muzzlePoint != null ? muzzlePoint.position : transform.position + Vector3.up * 1.5f;
            Vector3 aim = targetAimPosition - origin;
            if (aim.sqrMagnitude < 1e-6f) aim = transform.forward;
            Vector3 direction = ApplySpread(aim.normalized, aimErrorDegrees);
            ServerResolveShot(origin, direction, origin);
        }

        /// <summary>Server: alive, match running, not reloading and has ammo. An empty magazine starts a reload.</summary>
        private bool ServerCanShoot()
        {
            if (m_Health != null && m_Health.IsDead) return false;
            if (IsMatchOver || isReloading.Value) return false;
            if (currentAmmo.Value <= 0)
            {
                ServerStartReload();
                return false;
            }
            return true;
        }

        /// <summary>
        /// Server: the one place a shot is resolved, for players and bots alike. Spends a round,
        /// traces every pellet, applies damage to enemy players and stations, then tells clients.
        /// </summary>
        private void ServerResolveShot(Vector3 origin, Vector3 direction, Vector3 muzzlePos)
        {
            if (m_Health != null) m_Health.EndSpawnProtection();
            currentAmmo.Value = Mathf.Max(0, currentAmmo.Value - 1);

            Team myTeam = m_Player != null ? m_Player.CurrentTeam : Team.Neutral;
            bool hitAnyEnemy = false;
            bool killedAny = false;
            bool didHitAny = false;
            bool primaryHitCharacter = false;
            Vector3 primaryHitPoint = origin + direction * maxRange;
            Vector3 primaryHitNormal = -direction;

            for (int i = 0; i < bulletsPerShot; i++)
            {
                Vector3 shootDir = ApplySpread(direction, spreadAngle);
                if (!ConquestPhysics.RaycastIgnoring(transform, origin, shootDir, maxRange, hitLayers, out RaycastHit hit)) continue;

                didHitAny = true;
                if (i == 0)
                {
                    primaryHitPoint = hit.point;
                    primaryHitNormal = hit.normal;
                }

                // No friendly fire. Any health without a team (none today) is fair game.
                ConquestHealth targetHealth = hit.collider.GetComponentInParent<ConquestHealth>();
                if (i == 0) primaryHitCharacter = targetHealth != null;
                if (targetHealth != null && targetHealth != m_Health)
                {
                    ConquestPlayer targetPlayer = targetHealth.GetComponent<ConquestPlayer>();
                    if (targetPlayer == null || targetPlayer.CurrentTeam != myTeam)
                    {
                        bool wasAlive = !targetHealth.IsDead;
                        targetHealth.TakeDamage(damagePerShot, NetworkObjectId, origin);
                        hitAnyEnemy = true;
                        killedAny |= wasAlive && targetHealth.IsDead;
                    }
                    continue;
                }

                ConquestSupportStation station = hit.collider.GetComponentInParent<ConquestSupportStation>();
                if (station != null && station.stationTeam.Value != myTeam)
                {
                    station.TakeDamage(damagePerShot);
                    hitAnyEnemy = true;
                }
            }

            // Hit confirmation goes to a human shooter only; a bot's owner is the host.
            if (hitAnyEnemy && !IsBot)
            {
                ConfirmHitClientRpc(killedAny, m_OwnerRpcParams);
            }

            FireClientRpc(muzzlePos, primaryHitPoint, primaryHitNormal, didHitAny, primaryHitCharacter);

            if (currentAmmo.Value <= 0)
            {
                ServerStartReload();
            }
        }

        /// <summary>
        /// Never trust the client's shot: enforce the class fire rate, reject malformed rays and
        /// origins far from where the server thinks this player's camera is.
        /// </summary>
        private bool ServerValidateShot(ref Vector3 rayOrigin, ref Vector3 rayDirection)
        {
            if (!IsFinite(rayOrigin) || !IsFinite(rayDirection) || rayDirection.sqrMagnitude < 1e-6f) return false;
            rayDirection.Normalize();

            // Movement is owner-authoritative, so the server's copy lags slightly; the tolerance covers that.
            Vector3 expectedOrigin = (m_Movement != null && m_Movement.PlayerCamera != null)
                ? m_Movement.PlayerCamera.transform.position
                : transform.position + Vector3.up * 1.6f;
            if ((rayOrigin - expectedOrigin).sqrMagnitude > maxRayOriginOffset * maxRayOriginOffset) return false;

            // Rate limit that tolerates bunched-up packets but holds the long-run average to fireRate.
            float now = Time.time;
            if (now < m_ServerNextFireTime - fireTimingTolerance) return false;
            m_ServerNextFireTime = Mathf.Max(now, m_ServerNextFireTime) + fireRate;
            return true;
        }

        private static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                     float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }

        [ClientRpc]
        private void FireClientRpc(Vector3 muzzlePos, Vector3 hitPoint, Vector3 hitNormal, bool didHit, bool hitCharacter)
        {
            // The human shooter already drew this shot. Bots are owned by the host, so the
            // host still needs to see and hear their fire.
            if (PredictsLocally) return;

            PlayMuzzleFlash();
            SpawnLocalTracer(muzzlePos, hitPoint);
            PlayFireAudio();
            m_Animator?.TriggerFire();

            if (didHit) PlayImpact(hitPoint, hitNormal, hitCharacter);
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

        private void TryStartReload()
        {
            if (isReloading.Value || currentAmmo.Value >= magazineCapacity) return;

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

        /// <summary>Server: start a reload unless one is running or the magazine is full. Only one runs at a time.</summary>
        private void ServerStartReload()
        {
            if (m_ServerReload != null || isReloading.Value || currentAmmo.Value >= magazineCapacity) return;
            m_ServerReload = StartCoroutine(ServerReloadCoroutine());
        }

        /// <summary>
        /// Server: drop any reload in progress and refill the magazine. Called on death, respawn,
        /// class change, match end and despawn so a dead or reset player never keeps a ghost reload.
        /// </summary>
        public void ServerResetAmmo()
        {
            if (!IsServer) return;
            if (m_ServerReload != null)
            {
                StopCoroutine(m_ServerReload);
                m_ServerReload = null;
            }
            isReloading.Value = false;
            currentAmmo.Value = magazineCapacity;
        }

        private IEnumerator ServerReloadCoroutine()
        {
            isReloading.Value = true;

            yield return new WaitForSeconds(reloadDuration);

            currentAmmo.Value = magazineCapacity;
            isReloading.Value = false;
            m_ServerReload = null;
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

        /// <summary>Server: a full magazine now, cutting short any reload in progress.</summary>
        public void ResupplyAmmo()
        {
            if (!IsServer) return;
            if (isReloading.Value || currentAmmo.Value < magazineCapacity) ServerResetAmmo();
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

        // Characters and surfaces get different effects (energy sparks vs. dust and sparks).
        private void PlayImpact(Vector3 point, Vector3 normal, bool hitCharacter)
        {
            GameObject prefab = hitCharacter && characterImpactPrefab != null ? characterImpactPrefab : hitImpactPrefab;
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
            ConquestFxPool.Play(prefab, point, Quaternion.LookRotation(normal), impactSeconds);
        }

        private void SpawnLocalTracer(Vector3 from, Vector3 to)
        {
            Material tracerMaterial = m_Player != null && m_Player.CurrentTeam == Team.Blue ? blueTracerMaterial : redTracerMaterial;
            ConquestTracerPool.Show(from, to, tracerMaterial, tracerWidth, tracerDuration);
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


