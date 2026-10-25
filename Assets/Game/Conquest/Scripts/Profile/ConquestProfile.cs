using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Game.Conquest.Gameplay;

namespace Game.Conquest.Profile
{
    /// <summary>Lifetime totals for one class.</summary>
    [Serializable]
    public class ClassStats
    {
        public string className;
        public int matches;
        public int kills;
        public int deaths;
        public float secondsPlayed;
    }

    /// <summary>
    /// A player's profile: name, experience and lifetime stats. Saved as JSON on this PC for now;
    /// the same data moves to Unity Cloud Save later (see the online play plan).
    /// </summary>
    [Serializable]
    public class ConquestProfile
    {
        public int version = 1;
        public string playerName = "";
        public int experience;
        public int matchesPlayed;
        public int wins;
        public int losses;
        public int kills;
        public int deaths;
        public int captures;
        public float secondsPlayed;
        public int bestMatchKills;
        public List<ClassStats> classes = new List<ClassStats>();

        public ClassStats ForClass(string className)
        {
            foreach (var c in classes) if (c.className == className) return c;
            var stats = new ClassStats { className = className };
            classes.Add(stats);
            return stats;
        }
    }

    /// <summary>Loads, saves and updates the local profile.</summary>
    public static class ProfileStore
    {
        private const string FileName = "profile.json";
        private static ConquestProfile s_Profile;
        private static MatchResults s_LastRecorded;

        /// <summary>Raised after the profile changes (a match recorded, a name set).</summary>
        public static event Action Changed;

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static ConquestProfile Current
        {
            get
            {
                if (s_Profile == null) s_Profile = Load();
                return s_Profile;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Profile = null;
            s_LastRecorded = null;
            Changed = null;
        }

        private static ConquestProfile Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var loaded = JsonUtility.FromJson<ConquestProfile>(File.ReadAllText(FilePath));
                    if (loaded != null) return loaded;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ProfileStore] Couldn't read the profile, starting a new one: {e.Message}");
            }
            return new ConquestProfile();
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ProfileStore] Couldn't save the profile: {e.Message}");
            }
            Changed?.Invoke();
        }

        public static void SetName(string name)
        {
            Current.playerName = name ?? "";
            Save();
        }

        /// <summary>
        /// Adds the local player's line from a finished match. Each results object counts once, and
        /// a match the local player wasn't in (a dedicated server, a spectator) adds nothing.
        /// </summary>
        public static bool RecordMatch(MatchResults results, ulong localClientId, ProfileRules rules, Func<int, string> className)
        {
            if (results == null || ReferenceEquals(results, s_LastRecorded)) return false;
            MatchResultRow? mine = null;
            foreach (var row in results.rows)
            {
                if (!row.isBot && row.clientId == localClientId) { mine = row; break; }
            }
            if (mine == null) return false;
            s_LastRecorded = results;

            var r = mine.Value;
            var p = Current;
            bool won = results.winner != Team.Neutral && results.winner == r.team;
            bool lost = results.winner != Team.Neutral && results.winner != r.team;
            p.matchesPlayed++;
            if (won) p.wins++;
            if (lost) p.losses++;
            p.kills += r.kills;
            p.deaths += r.deaths;
            p.captures += r.captures;
            p.secondsPlayed += results.durationSeconds;
            p.bestMatchKills = Mathf.Max(p.bestMatchKills, r.kills);

            // The results carry the class the player ended on, so the whole match counts for it.
            var cls = p.ForClass(className != null ? className(r.classIndex) : $"Class {r.classIndex}");
            cls.matches++;
            cls.kills += r.kills;
            cls.deaths += r.deaths;
            cls.secondsPlayed += results.durationSeconds;

            if (rules != null) p.experience += rules.ExperienceFor(r.kills, r.captures, won);
            Save();
            return true;
        }
    }
}
