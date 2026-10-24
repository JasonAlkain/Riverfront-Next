using System.Collections.Generic;
using UnityEngine;

namespace Game.Conquest.Combat
{
    /// <summary>
    /// Reuses short-lived effect instances (muzzle flashes, impacts) instead of Instantiate/Destroy
    /// per shot. Each prefab gets a fixed number of instances; when all are busy the oldest is
    /// restarted, which also caps how many of one effect can play at once. Purely presentation:
    /// an effect with an AudioSource (Play On Awake off) plays its clip through the audio limiter,
    /// a few voices at a time per effect, so a hail of impacts can't take every voice.
    /// </summary>
    public class ConquestFxPool : MonoBehaviour
    {
        // Most instances of one effect alive at once (placeholder, tune with profiling).
        private const int PerPrefab = 24;

        // Placeholder: sounds of one effect that may start in one 50 ms window.
        private const int VoicesPerEffect = 3;

        private class Pool
        {
            public readonly GameObject[] instances = new GameObject[PerPrefab];
            public readonly AudioSource[] sounds = new AudioSource[PerPrefab];
            public readonly float[] hideAt = new float[PerPrefab];
            public int next;
            public string audioCategory;
        }

        private static ConquestFxPool s_Instance;
        private readonly Dictionary<GameObject, Pool> m_Pools = new Dictionary<GameObject, Pool>();
        private readonly List<Pool> m_PoolList = new List<Pool>();

        /// <summary>Play <paramref name="prefab"/> at a pose for <paramref name="lifetime"/> seconds. Null prefabs are ignored.</summary>
        public static void Play(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime)
        {
            if (prefab == null) return;
            EnsureInstance();
            s_Instance.PlayPooled(prefab, position, rotation, lifetime);
        }

        /// <summary>Create up to <paramref name="count"/> instances of an effect now (inactive), so its first play doesn't hitch.</summary>
        public static void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null) return;
            EnsureInstance();
            Pool pool = s_Instance.GetPool(prefab);
            for (int i = 0; i < Mathf.Min(count, PerPrefab); i++)
            {
                if (pool.instances[i] != null) continue;
                s_Instance.Create(prefab, pool, i).SetActive(false);
            }
        }

        private static void EnsureInstance()
        {
            if (s_Instance != null) return;
            var root = new GameObject("ConquestFxPool");
            DontDestroyOnLoad(root);
            s_Instance = root.AddComponent<ConquestFxPool>();
        }

        private Pool GetPool(GameObject prefab)
        {
            if (!m_Pools.TryGetValue(prefab, out Pool pool))
            {
                pool = new Pool { audioCategory = "Fx_" + prefab.name };
                m_Pools.Add(prefab, pool);
                m_PoolList.Add(pool);
            }
            return pool;
        }

        private GameObject Create(GameObject prefab, Pool pool, int index)
        {
            GameObject fx = Instantiate(prefab, transform);
            pool.instances[index] = fx;
            pool.sounds[index] = fx.GetComponentInChildren<AudioSource>(true);
            return fx;
        }

        private void PlayPooled(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime)
        {
            Pool pool = GetPool(prefab);
            int index = pool.next;
            pool.next = (pool.next + 1) % PerPrefab;

            GameObject fx = pool.instances[index];
            if (fx == null) fx = Create(prefab, pool, index);

            fx.transform.SetPositionAndRotation(position, rotation);
            // Re-activating restarts particle systems that play on awake.
            fx.SetActive(false);
            fx.SetActive(true);
            pool.hideAt[index] = Time.time + lifetime;

            AudioSource sound = pool.sounds[index];
            if (sound != null && sound.clip != null && !sound.playOnAwake)
            {
                Game.Conquest.Audio.ConquestAudioLimiter.TryPlayOneShot(sound, sound.clip, pool.audioCategory, 1f, 0.05f, 0.95f, 1.05f, VoicesPerEffect);
            }
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
