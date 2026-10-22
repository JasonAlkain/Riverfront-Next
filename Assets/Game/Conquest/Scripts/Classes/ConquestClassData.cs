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

        [Header("Special / Equipment")]
        public bool hasZoom = false;
        [Tooltip("Placeholder: how much aiming magnifies the view (1.25 = slight, 3 = long scope). Scales with the player's FOV setting.")]
        public float zoomMagnification = 1.25f;
        [Tooltip("Aiming shows a full-screen scope overlay instead of the crosshair.")]
        public bool hasScope = false;
        public bool hasSupportStation = false; // Deployable station for Engineer
        [Tooltip("Standing next to a damaged or broken supply droid repairs it.")]
        public bool canRepair = false;

        [Header("Audio")]
        public AudioClip fireSound;
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
            if (coolDelaySeconds <= fireRate)
            {
                Debug.LogWarning($"[ConquestClassData] {name}: coolDelaySeconds ({coolDelaySeconds}) is not above fireRate ({fireRate}), so the weapon cools between shots and never overheats.", this);
            }
        }
    }
}
