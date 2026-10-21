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

        [Header("Special / Equipment")]
        public bool hasZoom = false;
        [Tooltip("Placeholder: how much aiming magnifies the view (1.25 = slight, 3 = long scope). Scales with the player's FOV setting.")]
        public float zoomMagnification = 1.25f;
        [Tooltip("Aiming shows a full-screen scope overlay instead of the crosshair.")]
        public bool hasScope = false;
        public bool hasSupportStation = false; // Deployable station for Engineer

        [Header("Audio")]
        public AudioClip fireSound;
        public AudioClip reloadSound;
        public float firePitchMin = 0.95f;
        public float firePitchMax = 1.05f;
        public float fireVolume = 1.0f;
    }
}
