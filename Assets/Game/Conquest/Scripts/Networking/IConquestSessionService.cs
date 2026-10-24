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
        /// <summary>Online match: the host's Relay join code. Empty for a LAN / direct IP connection.</summary>
        public string joinCode;

        public ConquestConnectionData(string address, ushort port)
        {
            this.address = address;
            this.port = port;
            joinCode = "";
        }

        public static ConquestConnectionData ForJoinCode(string code)
        {
            return new ConquestConnectionData("", 0) { joinCode = code ?? "" };
        }

        public bool IsOnline => !string.IsNullOrEmpty(joinCode);
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

        /// <summary>An online join failed before connecting (unknown join code, no internet), with the reason.</summary>
        event Action<string> OnOnlineJoinFailed;

        /// <summary>How other players reach this host, as a lobby line (a join code, or LAN addresses).</summary>
        string HostJoinAddress { get; }

        /// <summary>
        /// What this peer can share so friends join the same match: the join code of an online match
        /// (host or client), or the host's LAN address. False when there's nothing to share.
        /// </summary>
        bool TryGetShareInfo(out string label, out string shown, out string copyText);

        void StartSoloGame(string mapSceneName, int tickets, int teamSize, BotDifficulty difficulty);
        bool StartHostGame(string mapId, string mapSceneName, string sessionName, int maxPlayers, int tickets, int teamSize, bool botFill, BotDifficulty difficulty);

        /// <summary>
        /// Hosts an online match through Relay (no public IP or open port). Signing in and reserving
        /// the relay take a moment, so <paramref name="done"/> reports the outcome: true once hosting,
        /// or false with a reason to show.
        /// </summary>
        void StartOnlineHostGame(string mapId, string mapSceneName, string sessionName, int maxPlayers, int tickets, int teamSize, bool botFill, BotDifficulty difficulty, Action<bool, string> done);

        /// <summary>Starts a server with no local player once the current scene has finished loading. Quits the process on failure.</summary>
        void StartDedicatedServer(ConquestServerLaunchArgs args);

        /// <summary>
        /// Starts connecting. A LAN connection starts at once (returns false only if it can't). An online
        /// one joins the relay first; if that fails the status changes and the connection is abandoned,
        /// which the menus see as a failed connection.
        /// </summary>
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
