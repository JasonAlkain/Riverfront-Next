using Game.Conquest.Player;

namespace Game.Conquest.Gameplay
{
    /// <summary>
    /// The rules of one game mode: how reinforcements are set up and spent, and when a team wins.
    /// ConquestMatchManager owns the shared match plumbing (tickets, stats, kill feed, results,
    /// spawns) and asks the active mode for everything rule-specific, so a later mode (capture
    /// the flag, hunt) is a new class here rather than a fork of the match manager.
    /// Every method runs on the server only.
    /// </summary>
    public interface IConquestGameMode
    {
        /// <summary>Stable id, as listed in ConquestMapData.availableGameModes.</summary>
        string Id { get; }

        /// <summary>Seconds a combatant waits after dying before they can be back in the fight.</summary>
        float RespawnSeconds { get; }

        /// <summary>The match has just spawned: set starting tickets and reset mode state.</summary>
        void ServerBegin(ConquestMatchManager match, in ConquestMatchSettings settings);

        /// <summary>Once per frame while the match is running.</summary>
        void ServerTick(ConquestMatchManager match);

        /// <summary>A combatant died. <paramref name="killer"/> is null for falls and kill volumes.</summary>
        void ServerOnDeath(ConquestMatchManager match, ConquestPlayer victim, ConquestPlayer killer);

        /// <summary>
        /// A dead combatant wants back in. Returns true (and charges their team whatever it costs)
        /// if they may respawn now, false if their team can't afford it yet.
        /// </summary>
        bool ServerTrySpendRespawn(ConquestMatchManager match, ConquestPlayer combatant);
    }

    /// <summary>Picks the mode class for a mode id. Unknown or empty ids fall back to Conquest.</summary>
    public static class ConquestGameModes
    {
        public const string ConquestId = "Conquest";

        public static IConquestGameMode Create(string modeId, ConquestModeRules conquestRules)
        {
            if (!string.IsNullOrEmpty(modeId) && modeId != ConquestId)
            {
                UnityEngine.Debug.LogWarning($"[ConquestGameModes] Unknown game mode '{modeId}'; playing {ConquestId}.");
            }
            return new ConquestMode(conquestRules ?? new ConquestModeRules());
        }
    }
}
