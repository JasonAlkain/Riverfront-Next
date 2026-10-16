using System;
using System.Collections.Generic;
using UnityEngine;
using Game.Conquest.Gameplay;

namespace Game.Conquest.Networking
{
    /// <summary>
    /// Command-line options for a dedicated (headless) server, e.g.
    /// <c>ConquestServer.exe -server -map industrial_depot -port 7777 -maxPlayers 16 -tickets 250
    /// -teamSize 8 -bots 1 -botDifficulty Normal -name "LAN Battle" -minPlayers 2 -startCountdown 10</c>.
    /// Anything not given keeps the ConquestSessionManager default.
    /// </summary>
    public class ConquestServerLaunchArgs
    {
        public string mapId;
        public ushort? port;
        public int? maxPlayers;
        public int? tickets;
        public int? teamSize;
        public bool? bots;
        public BotDifficulty? botDifficulty;
        public string sessionName;
        public int? minPlayersToStart;
        public float? startCountdownSeconds;

        /// <summary>True when this process should run as a dedicated server.</summary>
        public static bool IsDedicatedServerLaunch()
        {
            // The Editor also defines UNITY_SERVER while the Dedicated Server build profile is
            // active, but Play mode there is still for playing, so only a real server build counts.
#if UNITY_SERVER && !UNITY_EDITOR
            return true;
#else
            return HasFlag(Environment.GetCommandLineArgs(), "-server");
#endif
        }

        public static ConquestServerLaunchArgs Parse(string[] args)
        {
            var result = new ConquestServerLaunchArgs();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].StartsWith("-") && !args[i + 1].StartsWith("-"))
                {
                    values[args[i]] = args[i + 1];
                }
            }

            if (values.TryGetValue("-map", out var map)) result.mapId = map;
            if (values.TryGetValue("-name", out var name)) result.sessionName = name;
            if (values.TryGetValue("-port", out var p) && ushort.TryParse(p, out var port) && port > 0) result.port = port;
            if (values.TryGetValue("-maxPlayers", out var mp) && int.TryParse(mp, out var maxPlayers)) result.maxPlayers = Mathf.Clamp(maxPlayers, 1, 64);
            if (values.TryGetValue("-tickets", out var t) && int.TryParse(t, out var tickets)) result.tickets = Mathf.Max(1, tickets);
            if (values.TryGetValue("-teamSize", out var ts) && int.TryParse(ts, out var teamSize)) result.teamSize = Mathf.Clamp(teamSize, 1, 32);
            if (values.TryGetValue("-bots", out var b)) result.bots = b == "1" || b.Equals("true", StringComparison.OrdinalIgnoreCase);
            if (values.TryGetValue("-botDifficulty", out var d) && Enum.TryParse(d, true, out BotDifficulty diff)) result.botDifficulty = diff;
            if (values.TryGetValue("-minPlayers", out var mn) && int.TryParse(mn, out var minPlayers)) result.minPlayersToStart = Mathf.Max(1, minPlayers);
            if (values.TryGetValue("-startCountdown", out var sc) && float.TryParse(sc, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var countdown)) result.startCountdownSeconds = Mathf.Max(0f, countdown);

            return result;
        }

        private static bool HasFlag(string[] args, string flag)
        {
            foreach (var a in args)
            {
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
