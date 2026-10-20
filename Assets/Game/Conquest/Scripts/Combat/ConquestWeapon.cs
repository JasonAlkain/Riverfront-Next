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
        [SerializeField] private LayerMask hitLayers = ~0;

        [Header("Special Abilities")]
        [SerializeField] private bool hasZoom = false;
        [SerializeField] private float zoomFOV = 35.0f;
        [SerializeField] private bool hasSupportStation = false;
        [SerializeField] private GameObject supportStationPrefab;
        [SerializeField] private float stationCooldown = 15.0f;

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
        [SerializeField] private GameObject hitImpactPrefab;
        [SerializeField] private AudioSource weaponAudioSource;
        [SerializeField] private AudioClip fireSoundClip;
        [SerializeField] private AudioClip reloadSoundClip;
        [SerializeField] private AudioClip targetHitClip;
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
        private float m_DefaultFOV = 75.0f;
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
            // A player who leaves (or is cleared at match end) takes their support station with them.
            if (IsServer && m_ServerActiveStation != null && m_ServerActiveStation.IsSpawned)
            {
                m_ServerActiveStation.Despawn(true);
            }
            m_ServerActiveStation = null;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                currentAmmo.Value = magazineCapacity;
                isReloading.Value = false;
            }

            if (m_Movement != null && m_Movement.PlayerCamera != null)
            {
                m_DefaultFOV = m_Movement.PlayerCamera.fieldOfView;
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

            if (IsServer)
            {
                currentAmmo.Value = magazineCapacity;
                isReloading.Value = false;
            }
        }

        private void Update()
        {
            if (!IsOwner) return;
            if (m_Health != null && m_Health.IsDead) return;
            if (IsMatchOver) return;
            if (ConquestPauseMenu.Instance != null && ConquestPauseMenu.Instance.IsPaused) return;

            HandleInput();
            HandleZoom();
        }

        // Firing and stations stop when the match ends, on the owner and again on the server.
        private static bool IsMatchOver =>
            ConquestMatchManager.Instance != null && ConquestMatchManager.Instance.IsMatchOver;

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

        private void HandleZoom()
        {
            if (!hasZoom || m_Movement == null || m_Movement.PlayerCamera == null) return;

            bool zoomHeld = Mouse.current != null && Mouse.current.rightButton.isPressed;
            m_IsZoomed = zoomHeld;

            float targetFOV = m_IsZoomed ? zoomFOV : m_DefaultFOV;
            m_Movement.PlayerCamera.fieldOfView = Mathf.Lerp(m_Movement.PlayerCamera.fieldOfView, targetFOV, Time.deltaTime * 12.0f);
        }

        private void TryFireLocal()
        {
            m_NextFireTime = Time.time + fireRate;

            Camera cam = m_Movement.PlayerCamera;
            if (cam == null) return;

            Vector3 origin = muzzlePoint != null ? muzzlePoint.position : cam.transform.position;
            Ray baseRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            // Visual feedback & tracer for all pellets
            for (int i = 0; i < bulletsPerShot; i++)
            {
                Vector3 shootDir = ApplySpread(baseRay.direction, spreadAngle);
                Vector3 targetPoint = origin + shootDir * maxRange;

                if (Physics.Raycast(origin, shootDir, out RaycastHit hit, maxRange, hitLayers, QueryTriggerInteraction.Ignore))
                {
                    targetPoint = hit.point;
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
            if (m_Health != null && m_Health.IsDead) return;
            if (IsMatchOver) return;
            if (isReloading.Value || currentAmmo.Value <= 0) return;
            if (!ServerValidateShot(ref rayOrigin, ref rayDirection)) return;
            if (m_Health != null) m_Health.EndSpawnProtection();

            currentAmmo.Value = Mathf.Max(0, currentAmmo.Value - 1);

            Vector3 muzzlePos = muzzlePoint != null ? muzzlePoint.position : rayOrigin;
            bool hitAnyEnemy = false;
            Vector3 primaryHitPoint = rayOrigin + rayDirection * maxRange;
            Vector3 primaryHitNormal = -rayDirection;
            bool didHitAny = false;

            for (int i = 0; i < bulletsPerShot; i++)
            {
                Vector3 shootDir = ApplySpread(rayDirection, spreadAngle);
                Vector3 hitPoint = rayOrigin + shootDir * maxRange;
                Vector3 hitNormal = -shootDir;

                if (Physics.Raycast(rayOrigin, shootDir, out RaycastHit hit, maxRange, hitLayers, QueryTriggerInteraction.Ignore))
                {
                    hitPoint = hit.point;
                    hitNormal = hit.normal;
                    didHitAny = true;
                    if (i == 0)
                    {
                        primaryHitPoint = hitPoint;
                        primaryHitNormal = hitNormal;
                    }

                    // Damage target
                    ConquestHealth targetHealth = hit.collider.GetComponentInParent<ConquestHealth>();
                    if (targetHealth != null && targetHealth != m_Health)
                    {
                        // Check team to avoid friendly fire
                        ConquestPlayer targetPlayer = hit.collider.GetComponentInParent<ConquestPlayer>();
                        if (targetPlayer == null || targetPlayer.CurrentTeam != m_Player.CurrentTeam)
                        {
                            targetHealth.TakeDamage(damagePerShot, NetworkObjectId);
                            hitAnyEnemy = true;
                        }
                    }

                    // Damage support stations
                    ConquestSupportStation station = hit.collider.GetComponentInParent<ConquestSupportStation>();
                    if (station != null && station.stationTeam.Value != m_Player.CurrentTeam)
                    {
                        station.TakeDamage(damagePerShot);
                    }
                }
            }

            // Confirm hit audio to attacker ONCE per trigger
            if (hitAnyEnemy)
            {
                ConfirmHitClientRpc(new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } } });
            }

            // Broadcast firing audio and tracer ONCE to remote clients
            FireClientRpc(muzzlePos, primaryHitPoint, primaryHitNormal, didHitAny);

            // Auto-reload when dry
            if (currentAmmo.Value <= 0)
            {
                StartCoroutine(ServerReloadCoroutine());
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

        // Server-side execution for AI soldiers
        public void FireFromAI(Vector3 targetAimPosition)
        {
            if (!IsServer || (m_Health != null && m_Health.IsDead)) return;
            if (IsMatchOver) return;
            if (isReloading.Value) return;
            if (m_Health != null) m_Health.EndSpawnProtection();

            if (currentAmmo.Value <= 0)
            {
                StartCoroutine(ServerReloadCoroutine());
                return;
            }

            if (Time.time < m_NextFireTime) return;
            m_NextFireTime = Time.time + fireRate;

            currentAmmo.Value = Mathf.Max(0, currentAmmo.Value - 1);

            Vector3 origin = muzzlePoint != null ? muzzlePoint.position : transform.position + Vector3.up * 1.5f;
            Vector3 aimDirection = (targetAimPosition - origin).normalized;
            Vector3 primaryHitPoint = origin + aimDirection * maxRange;
            Vector3 primaryHitNormal = -aimDirection;
            bool didHitAny = false;

            for (int i = 0; i < bulletsPerShot; i++)
            {
                Vector3 shootDir = ApplySpread(aimDirection, spreadAngle);
                Vector3 hitPoint = origin + shootDir * maxRange;
                Vector3 hitNormal = -shootDir;

                if (Physics.Raycast(origin, shootDir, out RaycastHit hit, maxRange, hitLayers, QueryTriggerInteraction.Ignore))
                {
                    hitPoint = hit.point;
                    hitNormal = hit.normal;
                    didHitAny = true;
                    if (i == 0)
                    {
                        primaryHitPoint = hitPoint;
                        primaryHitNormal = hitNormal;
                    }

                    ConquestHealth targetHealth = hit.collider.GetComponentInParent<ConquestHealth>();
                    if (targetHealth != null && targetHealth != m_Health)
                    {
                        ConquestPlayer targetPlayer = hit.collider.GetComponentInParent<ConquestPlayer>();
                        if (targetPlayer == null || targetPlayer.CurrentTeam != m_Player.CurrentTeam)
                        {
                            targetHealth.TakeDamage(damagePerShot, NetworkObjectId);
                        }
                    }
                }
            }

            // Broadcast fire audio and visual feedback ONCE to clients
            FireClientRpc(origin, primaryHitPoint, primaryHitNormal, didHitAny);

            if (currentAmmo.Value <= 0)
            {
                StartCoroutine(ServerReloadCoroutine());
            }
        }

        [ClientRpc]
        private void FireClientRpc(Vector3 muzzlePos, Vector3 hitPoint, Vector3 hitNormal, bool didHit)
        {
            if (IsOwner) return; // Local owner already rendered prediction

            SpawnLocalTracer(muzzlePos, hitPoint);
            PlayFireAudio();
            m_Animator?.TriggerFire();

            if (didHit && hitImpactPrefab != null)
            {
                GameObject fx = Instantiate(hitImpactPrefab, hitPoint, Quaternion.LookRotation(hitNormal));
                Destroy(fx, 1.2f);
            }
        }

        [ClientRpc]
        private void ConfirmHitClientRpc(ClientRpcParams clientRpcParams = default)
        {
            ConquestHUD.TriggerHitmarker();
            if (weaponAudioSource != null && targetHitClip != null)
            {
                // Play subtle hit confirmation using the audio limiter
                ConquestAudioLimiter.TryPlayHitFeedback(weaponAudioSource, targetHitClip, 0.16f);
            }
        }

        private void TryStartReload()
        {
            if (isReloading.Value || currentAmmo.Value >= magazineCapacity) return;

            m_Animator?.TriggerReload();

            if (IsServer)
            {
                StartCoroutine(ServerReloadCoroutine());
            }
            else
            {
                RequestReloadServerRpc();
            }
        }

        [ServerRpc]
        private void RequestReloadServerRpc()
        {
            if (!isReloading.Value && currentAmmo.Value < magazineCapacity)
            {
                StartCoroutine(ServerReloadCoroutine());
            }
        }

        private IEnumerator ServerReloadCoroutine()
        {
            isReloading.Value = true;
            PlayReloadClientRpc();

            yield return new WaitForSeconds(reloadDuration);

            currentAmmo.Value = magazineCapacity;
            isReloading.Value = false;
        }

        [ClientRpc]
        private void PlayReloadClientRpc()
        {
            m_Animator?.TriggerReload();
            if (weaponAudioSource != null && reloadSoundClip != null)
            {
                weaponAudioSource.pitch = 1.0f;
                weaponAudioSource.PlayOneShot(reloadSoundClip, 0.65f);
            }
        }

        public void ResupplyAmmo()
        {
            if (IsServer)
            {
                currentAmmo.Value = magazineCapacity;
            }
        }

        private void TryDeployStation()
        {
            if (Time.time < m_NextStationDeployTime) return;
            m_NextStationDeployTime = Time.time + stationCooldown;

            DeployStationServerRpc();
        }

        [ServerRpc]
        private void DeployStationServerRpc()
        {
            if (supportStationPrefab == null || m_Player == null) return;
            // Capability, alive state and cooldown are checked here, not just on the client.
            if (!hasSupportStation) return;
            if (m_Health != null && m_Health.IsDead) return;
            if (IsMatchOver) return;
            if (Time.time < m_ServerNextStationTime) return;
            if (m_Health != null) m_Health.EndSpawnProtection();
            m_ServerNextStationTime = Time.time + stationCooldown;

            // One active station per player: deploying again replaces the old one.
            if (m_ServerActiveStation != null && m_ServerActiveStation.IsSpawned)
            {
                m_ServerActiveStation.Despawn(true);
            }
            m_ServerActiveStation = null;

            // Position comes from the server's view of the player, never from the client.
            Vector3 position = transform.position + transform.forward * stationDeployDistance + Vector3.up * 0.1f;
            GameObject stationObj = Instantiate(supportStationPrefab, position, transform.rotation);
            var netObj = stationObj.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                netObj.Spawn();
                m_ServerActiveStation = netObj;
                var stationComp = stationObj.GetComponent<ConquestSupportStation>();
                if (stationComp != null)
                {
                    stationComp.stationTeam.Value = m_Player.CurrentTeam;
                }
            }
        }

        private void SpawnLocalTracer(Vector3 from, Vector3 to)
        {
            if (muzzleFlashPrefab != null && muzzlePoint != null)
            {
                GameObject flash = Instantiate(muzzleFlashPrefab, muzzlePoint.position, muzzlePoint.rotation, muzzlePoint);
                Destroy(flash, 0.15f);
            }

            GameObject tracerGO = new GameObject("LaserTracer");
            LineRenderer lr = tracerGO.AddComponent<LineRenderer>();
            lr.startWidth = 0.05f;
            lr.endWidth = 0.05f;
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);

            Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            mat.color = m_Player != null && m_Player.CurrentTeam == Team.Blue ? new Color(0.2f, 0.6f, 1f) : new Color(1f, 0.2f, 0.2f);
            lr.material = mat;

            Destroy(tracerGO, 0.06f);
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


