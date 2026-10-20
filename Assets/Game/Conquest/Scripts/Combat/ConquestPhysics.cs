using UnityEngine;

namespace Game.Conquest.Combat
{
    /// <summary>
    /// Physics layers used by Conquest and ray queries that skip the caller's own colliders.
    /// Every character shares the Player layer, so a layer mask alone can't stop a shot or a
    /// line-of-sight check from hitting the shooter's own body; these helpers filter it out.
    /// </summary>
    public static class ConquestPhysics
    {
        // Must match ProjectSettings/TagManager.asset.
        public const int PlayerLayer = 6;
        public const int DeployableLayer = 7;

        /// <summary>World geometry, characters and deployables: what shots and bot sight lines stop at.</summary>
        public const int ShotMask = (1 << 0) | (1 << PlayerLayer) | (1 << DeployableLayer);

        // Shared by every query on the main thread; results are consumed before the next call.
        private static readonly RaycastHit[] s_Hits = new RaycastHit[32];

        /// <summary>Nearest solid hit along the ray that isn't part of <paramref name="ignoreRoot"/>.</summary>
        public static bool RaycastIgnoring(Transform ignoreRoot, Vector3 origin, Vector3 direction, float maxDistance,
                                           int layerMask, out RaycastHit nearest)
        {
            RaycastHit[] hits = s_Hits;
            int count = Physics.RaycastNonAlloc(origin, direction, hits, maxDistance, layerMask, QueryTriggerInteraction.Ignore);

            // A full buffer may have dropped the nearest hit (it keeps an arbitrary subset), so
            // fall back to the allocating query in that rare case.
            if (count == hits.Length)
            {
                hits = Physics.RaycastAll(origin, direction, maxDistance, layerMask, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }

            // Hits come back in no particular order.
            int best = -1;
            for (int i = 0; i < count; i++)
            {
                if (ignoreRoot != null && hits[i].collider.transform.IsChildOf(ignoreRoot)) continue;
                if (best < 0 || hits[i].distance < hits[best].distance) best = i;
            }

            nearest = best >= 0 ? hits[best] : default;
            return best >= 0;
        }

        /// <summary>Like Physics.Linecast, but the caller's own colliders never block the line.</summary>
        public static bool LinecastIgnoring(Transform ignoreRoot, Vector3 from, Vector3 to, int layerMask, out RaycastHit nearest)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 1e-4f)
            {
                nearest = default;
                return false;
            }
            return RaycastIgnoring(ignoreRoot, from, delta / distance, distance, layerMask, out nearest);
        }
    }
}
