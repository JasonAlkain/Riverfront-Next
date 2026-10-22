using UnityEngine;

namespace Game.Conquest.Classes
{
    /// <summary>Network and presentation tuning for travelling bolts, shared by every class and team.</summary>
    [CreateAssetMenu(fileName = "BoltSettings", menuName = "Conquest/Bolt Settings")]
    public class ConquestBoltSettings : ScriptableObject
    {
        [Header("Server")]
        [Tooltip("Placeholder: most seconds a remote player's bolt is moved ahead on arrival to make up for their latency (half their round trip, measured by the server).")]
        public float maxFastForwardSeconds = 0.1f;
        [Tooltip("Placeholder: the server overheats a weapon this many shots before the owner's own count, so it never lags the owner's prediction.")]
        public float heatToleranceShots = 0.5f;

        [Header("Visuals")]
        [Tooltip("Placeholder: metres over which a drawn bolt merges from the muzzle onto the aim line.")]
        public float convergeDistance = 8f;
        [Tooltip("Placeholder: seconds over which another player's bolt eases from their muzzle to its true position.")]
        public float catchUpSeconds = 0.12f;

        [Header("Target velocity (bot leading)")]
        [Tooltip("Placeholder: smoothing time of the server's estimate of each combatant's velocity.")]
        public float velocitySmoothingSeconds = 0.15f;
        [Tooltip("Placeholder: a move larger than this in one frame (a respawn) resets the estimate.")]
        public float teleportResetDistance = 3f;
        [Tooltip("Placeholder: the estimate is clamped to this many times the sprint speed.")]
        public float velocityClampFactor = 1.25f;

        /// <summary>Defaults used when no settings asset is assigned.</summary>
        public static ConquestBoltSettings Fallback
        {
            get
            {
                if (s_Fallback == null) s_Fallback = CreateInstance<ConquestBoltSettings>();
                return s_Fallback;
            }
        }
        private static ConquestBoltSettings s_Fallback;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Fallback = null;
        }
    }
}
