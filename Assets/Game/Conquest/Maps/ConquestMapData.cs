using UnityEngine;

namespace Game.Conquest.Maps
{
    [CreateAssetMenu(fileName = "NewConquestMap", menuName = "Conquest/Map Data")]
    public class ConquestMapData : ScriptableObject
    {
        [Header("Identity")]
        public string mapId = "map_unique_id";
        public string displayName = "Battlefield Name";
        [TextArea(2, 4)]
        public string description = "Map tactical description.";
        public string sceneName = "SceneName";
        public Sprite previewImage;
        public Color themeColor = new Color(0.2f, 0.6f, 1f);

        [Header("Tactical Specs")]
        public int recommendedPlayers = 16;
        public int commandPostsCount = 5;
        public string[] availableGameModes = new string[] { "Conquest" };

        [Header("Default Parameters")]
        public int defaultTickets = 250;
        public int defaultBotsPerTeam = 4;
    }
}
