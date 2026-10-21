using System;
using UnityEngine;
using Game.Conquest.Classes;

namespace Game.Conquest.Gameplay
{
    /// <summary>
    /// How a bot of one class fights. Engineers also drop stations at posts they hold and when
    /// hurt; that comes from the class's station, not from here. All values are placeholders.
    /// </summary>
    [Serializable]
    public struct ConquestBotClassRole
    {
        public InfantryClassType classType;
        [Tooltip("Placeholder: metres the bot likes to fight from. Further away it closes in (if it advances).")]
        public float preferredRange;
        [Tooltip("False holds position and shoots from wherever it is, like a marksman on a sightline.")]
        public bool advancesOnTarget;
        [Tooltip("Placeholder: seconds the bot keeps firing at where a target vanished, to pin it down.")]
        public float suppressSeconds;
        [Tooltip("Placeholder: metres a defending slot counts as closer for this class, so it is picked to hold posts.")]
        public float defendPreference;

        public static ConquestBotClassRole[] Defaults => new[]
        {
            new ConquestBotClassRole { classType = InfantryClassType.Assault, preferredRange = 18f, advancesOnTarget = true },
            new ConquestBotClassRole { classType = InfantryClassType.Heavy, preferredRange = 15f, advancesOnTarget = true, suppressSeconds = 1.5f },
            new ConquestBotClassRole { classType = InfantryClassType.Engineer, preferredRange = 18f, advancesOnTarget = true },
            new ConquestBotClassRole { classType = InfantryClassType.Recon, preferredRange = 35f, advancesOnTarget = false, defendPreference = 25f },
        };
    }
}
