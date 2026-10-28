using System;
using Game.Conquest.Networking;
using UnityEngine;

namespace Game.Conquest.Diagnostics
{
    public enum ConquestLogLevel
    {
        Verbose = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Off = 4
    }

    /// <summary>
    /// Leveled game log. Lines keep the "[Tag] message" shape the nettest tools search for.
    /// Default level: Verbose in the Editor and development builds, Info in release builds.
    /// Override with <c>-logLevel verbose|info|warning|error|off</c> on the command line.
    /// A dedicated server also stamps each line with UTC time, since its log is read after the fact;
    /// run it with <c>-logFile -</c> to write the log to stdout instead of a file.
    /// </summary>
    public static class ConquestLog
    {
        private static bool s_Initialised;
        private static ConquestLogLevel s_MinLevel;
        private static bool s_Timestamps;

        public static ConquestLogLevel MinLevel
        {
            get { Initialise(); return s_MinLevel; }
            set { Initialise(); s_MinLevel = value; }
        }

        public static bool IsEnabled(ConquestLogLevel level)
        {
            Initialise();
            return level != ConquestLogLevel.Off && level >= s_MinLevel;
        }

        /// <summary>Chatty detail (each bot spawn, each team assignment). Hidden in release builds.</summary>
        public static void Verbose(string tag, string message) => Write(ConquestLogLevel.Verbose, tag, message);

        /// <summary>Events worth keeping in a server log: connects, rejects, captures, kills, match end.</summary>
        public static void Info(string tag, string message) => Write(ConquestLogLevel.Info, tag, message);

        public static void Warning(string tag, string message) => Write(ConquestLogLevel.Warning, tag, message);

        public static void Error(string tag, string message) => Write(ConquestLogLevel.Error, tag, message);

        /// <summary>Parses a -logLevel value; null for anything unrecognised.</summary>
        public static ConquestLogLevel? ParseLevel(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            switch (value.Trim().ToLowerInvariant())
            {
                case "verbose": case "debug": case "all": return ConquestLogLevel.Verbose;
                case "info": return ConquestLogLevel.Info;
                case "warning": case "warn": return ConquestLogLevel.Warning;
                case "error": return ConquestLogLevel.Error;
                case "off": case "none": return ConquestLogLevel.Off;
                default: return null;
            }
        }

        /// <summary>The line as written, minus the timestamp. Kept separate so tests can check it.</summary>
        public static string Format(string tag, string message) =>
            string.IsNullOrEmpty(tag) ? message : $"[{tag}] {message}";

        private static void Write(ConquestLogLevel level, string tag, string message)
        {
            if (!IsEnabled(level)) return;
            string line = Format(tag, message);
            if (s_Timestamps) line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z {LevelLabel(level)} {line}";

            switch (level)
            {
                case ConquestLogLevel.Error: Debug.LogError(line); break;
                case ConquestLogLevel.Warning: Debug.LogWarning(line); break;
                default: Debug.Log(line); break;
            }
        }

        private static string LevelLabel(ConquestLogLevel level) => level switch
        {
            ConquestLogLevel.Verbose => "VERBOSE",
            ConquestLogLevel.Info => "INFO",
            ConquestLogLevel.Warning => "WARN",
            _ => "ERROR"
        };

        private static void Initialise()
        {
            if (s_Initialised) return;
            s_Initialised = true;
            s_MinLevel = Debug.isDebugBuild ? ConquestLogLevel.Verbose : ConquestLogLevel.Info;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!args[i].Equals("-logLevel", StringComparison.OrdinalIgnoreCase)) continue;
                var parsed = ParseLevel(args[i + 1]);
                if (parsed.HasValue) s_MinLevel = parsed.Value;
            }
            s_Timestamps = ConquestServerLaunchArgs.IsDedicatedServerLaunch();
        }

        // Domain reload may be off in the Editor; start each Play session from the defaults again.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_Initialised = false;
    }
}
