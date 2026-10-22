using System;
using Game.Conquest.Maps;

namespace Game.Conquest.Gameplay
{
    /// <summary>
    /// The one set of rules a match is played with. Defaulted from the chosen map, edited by the
    /// host (menu or lobby) or the dedicated-server launch args, and committed to the session when
    /// the match starts. The match and bot managers only read it.
    /// </summary>
    [Serializable]
    public struct ConquestMatchSettings
    {
        public int reinforcementTickets;
        /// <summary>Participants per team, humans and bots together.</summary>
        public int teamSize;
        public bool botFilling;
        public BotDifficulty botDifficulty;
        /// <summary>Game mode id (see ConquestGameModes). Empty plays Conquest.</summary>
        public string gameMode;

        /// <summary>Used when no map is known (for example a map scene opened directly in the Editor).</summary>
        public static ConquestMatchSettings Default => new ConquestMatchSettings
        {
            reinforcementTickets = 250,
            teamSize = 8,
            botFilling = true,
            botDifficulty = BotDifficulty.Normal,
            gameMode = ConquestGameModes.ConquestId,
        };

        public static ConquestMatchSettings ForMap(ConquestMapData map)
        {
            var settings = Default;
            if (map != null)
            {
                settings.reinforcementTickets = map.defaultTickets;
                settings.teamSize = map.defaultTeamSize;
                if (map.availableGameModes != null && map.availableGameModes.Length > 0) settings.gameMode = map.availableGameModes[0];
            }
            return settings;
        }
    }
}
