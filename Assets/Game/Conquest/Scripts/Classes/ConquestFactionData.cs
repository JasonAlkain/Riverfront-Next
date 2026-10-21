using UnityEngine;
using Game.Conquest.Gameplay;

namespace Game.Conquest.Classes
{
    /// <summary>How a weapon keeps firing. Values are stable: they are serialized.</summary>
    public enum ConquestWeaponModel : byte
    {
        /// <summary>A magazine of shots, then a reload.</summary>
        Magazine = 0,
        /// <summary>Every shot adds heat; too much locks the weapon until it vents. Never reloads.</summary>
        Overheat = 1
    }

    /// <summary>
    /// What sets one team apart from the other. Both teams use the same classes (and class
    /// indices); the faction decides the weapon model and the look of its bolts. Resolved from
    /// the player's replicated team when used, never cached, so every peer agrees.
    /// </summary>
    [CreateAssetMenu(fileName = "NewFaction", menuName = "Conquest/Faction Data")]
    public class ConquestFactionData : ScriptableObject
    {
        [Header("Identity")]
        public Team team = Team.Red;

        [Header("Weapons")]
        public ConquestWeaponModel weaponModel = ConquestWeaponModel.Magazine;
        [Tooltip("Placeholder: share of a full gauge one support station pulse vents (1 = fully, like blue's full refill).")]
        [Range(0f, 1f)] public float stationHeatVent = 1f;

        [Header("Bolt look")]
        [Tooltip("Shared material asset; never instanced at runtime.")]
        public Material boltMaterial;
        [Tooltip("Used instead when the colour-blind team palette is on. Empty uses the normal material.")]
        public Material boltMaterialColorBlind;
        [Tooltip("Placeholder: bolt width in metres.")]
        public float boltWidth = 0.05f;
        [Tooltip("Placeholder: drawn bolt length in metres.")]
        public float boltLength = 1.5f;

        [Header("Overheat feedback (optional)")]
        public AudioClip overheatClip;
        public AudioClip ventDoneClip;

        public Material BoltMaterial(bool colorBlind)
        {
            return colorBlind && boltMaterialColorBlind != null ? boltMaterialColorBlind : boltMaterial;
        }
    }
}
