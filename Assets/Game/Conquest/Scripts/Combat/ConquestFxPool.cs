using System.Collections.Generic;
using UnityEngine;

namespace Game.Conquest.Combat
{
    /// <summary>
    /// Reuses short-lived effect instances (muzzle flashes, impacts) instead of Instantiate/Destroy
    /// per shot. Each prefab gets a fixed number of instances; when all are busy the oldest is
    /// restarted, which also caps how many of one effect can play at once. Purely visual.
    /// </summary>
    public class ConquestFxPool : MonoBehaviour
    {
        // Most instances of one effect alive at once (placeholder, tune with profiling).
        private const int PerPrefab = 24;

        private class Pool
        {
            public readonly GameObject[] instances = new GameObject[PerPrefab];
            public readonly float[] hideAt = new float[PerPrefab];
            public int next;
        }

        private static ConquestFxPool s_Instance;
        private readonly Dictionary<GameObject, Pool> m_Pools = new Dictionary<GameObject, Pool>();
        private readonly List<Pool> m_PoolList = new List<Pool>();

        /// <summary>Play <paramref name="prefab"/> at a pose for <paramref name="lifetime"/> seconds. Null prefabs are ignored.</summary>
        public static void Play(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime)
        {
            if (prefab == null) return;
            if (s_Instance == null)
            {
                var root = new GameObject("ConquestFxPool");
                DontDestroyOnLoad(root);
                s_Instance = root.AddComponent<ConquestFxPool>();
            }
            s_Instance.PlayPooled(prefab, position, rotation, lifetime);
        }

        private void PlayPooled(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime)
        {
            if (!m_Pools.TryGetValue(prefab, out Pool pool))
            {
                pool = new Pool();
                m_Pools.Add(prefab, pool);
                m_PoolList.Add(pool);
            }

            int index = pool.next;
            pool.next = (pool.next + 1) % PerPrefab;

            GameObject fx = pool.instances[index];
            if (fx == null)
            {
                fx = Instantiate(prefab, transform);
                pool.instances[index] = fx;
            }

            fx.transform.SetPositionAndRotation(position, rotation);
            // Re-activating restarts particle systems that play on awake.
            fx.SetActive(false);
            fx.SetActive(true);
            pool.hideAt[index] = Time.time + lifetime;
        }

        private void Update()
        {
            float now = Time.time;
            for (int p = 0; p < m_PoolList.Count; p++)
            {
                Pool pool = m_PoolList[p];
                for (int i = 0; i < PerPrefab; i++)
                {
                    GameObject fx = pool.instances[i];
                    if (fx != null && fx.activeSelf && now >= pool.hideAt[i]) fx.SetActive(false);
                }
            }
        }

        private void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
        }
    }
}
