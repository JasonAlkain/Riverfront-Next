using UnityEngine;
using Game.Conquest.Gameplay;

namespace Game.Conquest.Classes
{
    public enum InfantryClassType
    {
        Assault = 0,
        Heavy = 1,
        Engineer = 2,
        Recon = 3
    }

    /// <summary>What a class's second weapon does. Values are stable: they are serialized.</summary>
    public enum SidearmKind : byte
    {
        None = 0,
        /// <summary>A second gun: fires bolts with its own stats, ammo and heat.</summary>
        Blaster = 1,
        /// <summary>Fires no bolts; repairs the supply droid it is aimed at.</summary>
        RepairTool = 2
    }

    /// <summary>What a class throws with the throw key. Values are stable: they are serialized.</summary>
    public enum ThrowableKind : byte
    {
        None = 0,
        Grenade = 1
    }

    /// <summary>
    /// A class's fixed sidearm. Players never pick weapons: every class carries exactly this one
    /// next to its primary. Bolt and heat fields mean the same as the primary's.
    /// </summary>
    [System.Serializable]
    public class ConquestSidearmData
    {
        public SidearmKind kind = SidearmKind.None;
        public string weaponName = "Sidearm";
        [Tooltip("Shown on the HUD while the sidearm is in hand.")]
        public Sprite icon;
        [Tooltip("Placeholder.")] public int damagePerShot = 18;
        [Tooltip("Placeholder.")] public float fireRate = 0.3f;
        [Tooltip("Placeholder. For a repair tool, its reach in metres.")] public float maxRange = 60f;
        [Tooltip("Placeholder.")] public float spreadAngle = 0.8f;
        [Tooltip("Placeholder.")] public int magazineCapacity = 12;
        [Tooltip("Placeholder.")] public float reloadDuration = 1.2f;
        public bool isAutomatic = false;
        [Tooltip("Placeholder: bolt speed in m/s.")] public float boltSpeed = 110f;
        [Tooltip("Placeholder: shots without pause that overheat it (heat factions).")] public int shotsToOverheat = 12;
        [Tooltip("Placeholder.")] public float coolDelaySeconds = 0.4f;
        [Tooltip("Placeholder.")] public float fullCoolSeconds = 1.2f;
        [Tooltip("Placeholder.")] public float overheatLockoutSeconds = 1.2f;
        [Tooltip("The sidearm can aim down its sights. A repair tool never aims.")]
        public bool canAim = true;
        [Tooltip("Placeholder: how much aiming magnifies the view.")]
        public float zoomMagnification = 1.1f;
        [Tooltip("Placeholder: spread while fully aimed, as a share of Spread Angle.")]
        [Range(0f, 1f)] public float adsSpreadMultiplier = 0.6f;
        [Tooltip("Repair tool only. Placeholder: droid health restored per use (one per Fire Rate seconds).")]
        public int repairPerUse = 10;
        [Tooltip("Played on every shot (or every use of a repair tool). Empty is silent: a sidearm never borrows the primary's sounds.")]
        public AudioClip fireSound;
        [Tooltip("Committed clip used when Fire Sound is missing (it comes from the local-only Free SFX Pack).")]
        public AudioClip fallbackFireSound;
        [Tooltip("Placeholder: volume of the sidearm's shot.")]
        [Range(0f, 1f)] public float fireVolume = 0.4f;
        public AudioClip reloadSound;
    }

    [CreateAssetMenu(fileName = "NewConquestClass", menuName = "Conquest/Class Data")]
    public class ConquestClassData : ScriptableObject
    {
        [Header("Identity")]
        public InfantryClassType classType;
        public string className;
        [TextArea(2, 4)]
        public string description;
        public Color classAccentColor = Color.white;

        [Header("Attributes")]
        public int maxHealth = 100;
        public float walkSpeed = 6.0f;
        public float sprintSpeed = 10.0f;

        [Header("Primary Weapon")]
        public string weaponName = "Blaster Rifle";
        [Tooltip("Shown on the HUD while the primary is in hand.")]
        public Sprite weaponIcon;
        public int damagePerShot = 24;
        public float fireRate = 0.15f;
        public float maxRange = 100.0f;
        public int bulletsPerShot = 1;      // > 1 for shotgun spread
        public float spreadAngle = 0.5f;     // Spread in degrees
        public int magazineCapacity = 30;
        public float reloadDuration = 1.8f;
        public bool isAutomatic = true;

        [Header("Bolt")]
        [Tooltip("Placeholder: bolt speed in m/s. A bolt flies for maxRange / boltSpeed seconds.")]
        public float boltSpeed = 120f;
        [Tooltip("Placeholder: bolt hit radius in metres. 0 is a thin ray (exact aim).")]
        public float boltRadius = 0f;

        [Header("Overheat (used when the team's faction overheats)")]
        [Tooltip("Placeholder: shots fired without pause that overheat the weapon (1-255).")]
        public int shotsToOverheat = 30;
        [Tooltip("Placeholder: seconds after the last shot before the weapon starts cooling. Keep it above fireRate or the weapon never overheats.")]
        public float coolDelaySeconds = 0.25f;
        [Tooltip("Placeholder: seconds to cool from fully hot to cold once cooling starts.")]
        public float fullCoolSeconds = 1.6f;
        [Tooltip("Placeholder: seconds the weapon is locked after overheating.")]
        public float overheatLockoutSeconds = 1.6f;
        [Tooltip("Placeholder: share of the gauge still hot when the overheat lock ends (0 = cold).")]
        [Range(0f, 1f)] public float heatAfterOverheat = 0f;

        [Header("Aim Down Sights")]
        [Tooltip("The primary can aim down its sights (hold Aim).")]
        public bool hasZoom = false;
        [Tooltip("Placeholder: how much aiming magnifies the view (1.25 = slight, 3 = long scope). Scales with the player's FOV setting.")]
        public float zoomMagnification = 1.25f;
        [Tooltip("Placeholder: spread while fully aimed, as a share of Spread Angle (0.5 = half). Each weapon keeps its own hip spread.")]
        [Range(0f, 1f)] public float adsSpreadMultiplier = 0.5f;

        [Header("Special / Equipment")]
        [Tooltip("Aiming shows a full-screen scope overlay instead of the crosshair.")]
        public bool hasScope = false;
        [Tooltip("Deploys a support station with the throw key. A class with a station carries no other throwable.")]
        public bool hasSupportStation = false;

        [Header("Sidearm (fixed, switched to with the Switch Weapon key)")]
        public ConquestSidearmData sidearm = new ConquestSidearmData();

        [Header("Throwable")]
        public ThrowableKind throwable = ThrowableKind.None;
        public string throwableName = "Grenade";
        [Tooltip("Thrown object: a NetworkObject with a ConquestGrenade, registered in the network prefab list.")]
        public GameObject throwablePrefab;
        [Tooltip("Placeholder: how many a fresh spawn carries. Supply droids and stations give them back one at a time.")]
        public int throwableCount = 2;
        [Tooltip("Placeholder: seconds between two throws.")]
        public float throwCooldown = 1f;
        [Tooltip("Placeholder: launch speed in m/s.")]
        public float throwSpeed = 16f;

        [Header("Audio")]
        public AudioClip fireSound;
        [Tooltip("Committed clip used when Fire Sound is missing (it comes from the local-only Free SFX Pack).")]
        public AudioClip fallbackFireSound;
        public AudioClip reloadSound;
        public float firePitchMin = 0.95f;
        public float firePitchMax = 1.05f;
        public float fireVolume = 1.0f;

        private void OnValidate()
        {
            boltSpeed = Mathf.Max(0.1f, boltSpeed);
            boltRadius = Mathf.Max(0f, boltRadius);
            // Both travel to the owner as one byte.
            shotsToOverheat = Mathf.Clamp(shotsToOverheat, 1, 255);
            magazineCapacity = Mathf.Clamp(magazineCapacity, 1, 255);
            if (sidearm != null)
            {
                sidearm.shotsToOverheat = Mathf.Clamp(sidearm.shotsToOverheat, 1, 255);
                sidearm.magazineCapacity = Mathf.Clamp(sidearm.magazineCapacity, 1, 255);
                sidearm.boltSpeed = Mathf.Max(0.1f, sidearm.boltSpeed);
            }
            throwableCount = Mathf.Clamp(throwableCount, 0, 255);
            if (coolDelaySeconds <= fireRate)
            {
                Debug.LogWarning($"[ConquestClassData] {name}: coolDelaySeconds ({coolDelaySeconds}) is not above fireRate ({fireRate}), so the weapon cools between shots and never overheats.", this);
            }
        }
    }
}
