using System.Collections.Generic;
using UnityEngine;

namespace Game.Conquest.Maps
{
    [CreateAssetMenu(fileName = "ConquestMapRegistry", menuName = "Conquest/Map Registry")]
    public class ConquestMapRegistry : ScriptableObject
    {
        [SerializeField] private List<ConquestMapData> registeredMaps = new List<ConquestMapData>();

        public IReadOnlyList<ConquestMapData> RegisteredMaps => registeredMaps;

        public ConquestMapData GetMapById(string mapId)
        {
            if (string.IsNullOrEmpty(mapId)) return GetDefaultMap();

            foreach (var m in registeredMaps)
            {
                if (m != null && m.mapId.Equals(mapId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return m;
                }
            }
            return GetDefaultMap();
        }

        public ConquestMapData GetMapBySceneName(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return GetDefaultMap();

            foreach (var m in registeredMaps)
            {
                if (m != null && m.sceneName.Equals(sceneName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return m;
                }
            }
            return GetDefaultMap();
        }

        public ConquestMapData GetDefaultMap()
        {
            if (registeredMaps != null && registeredMaps.Count > 0 && registeredMaps[0] != null)
            {
                return registeredMaps[0];
            }
            return null;
        }
    }
}
