using System;
using Game.Conquest.Gameplay;

namespace Game.Conquest.Networking
{
    /// <summary>
    /// Where a client connects to. Plain data so another backend (e.g. Steam) can carry its own
    /// identity, such as a Steam ID or auth ticket, without the menus changing.
    /// </summary>
    public struct ConquestConnectionData
    {
        public string address;
        public ushort port;

        public ConquestConnectionData(string address, ushort port)
        {
            this.address = address;
            this.port = port;
        }
    }

    /// <summary>
    /// The only way menus and bootstrap code start, join or leave a networked session. The current
    /// implementation is LAN / direct IP (<see cref="ConquestSessionManager"/>); a future backend
    /// registers itself in <see cref="ConquestSession.Current"/> instead and the UI stays the same.
    /// </summary>
    public interface IConquestSessionService
    {
        string StatusMessage { get; }
        event Action<string> OnStatusChanged;

        /// <summary>Where other players can reach this host, for showing in the lobby.</summary>
        string HostJoinAddress { get; }

        void StartSoloGame(string mapSceneName, int tickets, int botsPerTeam, BotDifficulty difficulty);
        bool StartHostGame(string mapId, string mapSceneName, string sessionName, int maxPlayers, int tickets, int teamSize, bool botFill, BotDifficulty difficulty);

        /// <summary>Starts a server with no local player once the current scene has finished loading. Quits the process on failure.</summary>
        void StartDedicatedServer(ConquestServerLaunchArgs args);

        bool StartClientConnect(ConquestConnectionData connection);

        /// <summary>Abandons a connection attempt that has not completed yet.</summary>
        void CancelConnect();

        void LeaveSession();
    }

    public static class ConquestSession
    {
        /// <summary>The active session backend, or null before the persistent services exist.</summary>
        public static IConquestSessionService Current { get; set; }
    }
}
