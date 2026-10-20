using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

namespace Game.Conquest.Gameplay
{
    /// <summary>One combatant's line on the scoreboard and the results screen.</summary>
    public struct MatchResultRow : INetworkSerializable, IEquatable<MatchResultRow>
    {
        public FixedString64Bytes playerName;
        /// <summary>Owning client for humans; ulong.MaxValue for bots.</summary>
        public ulong clientId;
        public Team team;
        public int classIndex;
        public bool isBot;
        public int kills;
        public int deaths;
        /// <summary>Command posts captured or neutralised while standing in the zone.</summary>
        public int captures;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref playerName);
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref team);
            serializer.SerializeValue(ref classIndex);
            serializer.SerializeValue(ref isBot);
            serializer.SerializeValue(ref kills);
            serializer.SerializeValue(ref deaths);
            serializer.SerializeValue(ref captures);
        }

        public bool Equals(MatchResultRow other)
        {
            return playerName.Equals(other.playerName) && clientId == other.clientId && team == other.team &&
                   classIndex == other.classIndex && isBot == other.isBot && kills == other.kills &&
                   deaths == other.deaths && captures == other.captures;
        }
    }

    /// <summary>
    /// Final match summary. Built by the server when the match ends and sent to every client,
    /// because the match objects that hold the live numbers are gone once the map unloads.
    /// </summary>
    public class MatchResults : INetworkSerializable
    {
        public int matchId;
        public Team winner;
        public int redTickets;
        public int blueTickets;
        public float durationSeconds;
        public FixedString64Bytes mapScene;
        public float returnToLobbySeconds;
        public List<MatchResultRow> rows = new List<MatchResultRow>();

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref matchId);
            serializer.SerializeValue(ref winner);
            serializer.SerializeValue(ref redTickets);
            serializer.SerializeValue(ref blueTickets);
            serializer.SerializeValue(ref durationSeconds);
            serializer.SerializeValue(ref mapScene);
            serializer.SerializeValue(ref returnToLobbySeconds);

            int count = rows.Count;
            serializer.SerializeValue(ref count);
            if (serializer.IsReader)
            {
                rows.Clear();
                for (int i = 0; i < count; i++)
                {
                    var row = new MatchResultRow();
                    serializer.SerializeValue(ref row);
                    rows.Add(row);
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    var row = rows[i];
                    serializer.SerializeValue(ref row);
                }
            }
        }
    }

    /// <summary>
    /// Server-side kill/death bookkeeping for the current match. Humans are keyed by client id so a
    /// player keeps their line across respawns; bots by NetworkObjectId (they respawn in place).
    /// Players who leave mid-match keep their line with the last known name and team.
    /// </summary>
    public class MatchStatsTracker
    {
        private readonly Dictionary<ulong, MatchResultRow> m_Rows = new Dictionary<ulong, MatchResultRow>();
        private readonly List<ulong> m_Order = new List<ulong>();
        // Replicated copy for the live scoreboard; index i always holds the row for m_Order[i].
        private NetworkList<MatchResultRow> m_Mirror;

        /// <summary>Server: keep this replicated list in step with the rows from now on.</summary>
        public void SetMirror(NetworkList<MatchResultRow> mirror)
        {
            m_Mirror = mirror;
            if (m_Mirror == null) return;
            m_Mirror.Clear();
            foreach (var key in m_Order) m_Mirror.Add(m_Rows[key]);
        }

        private const ulong BotKeyFlag = 1UL << 63;

        public static ulong KeyFor(Player.ConquestPlayer player)
        {
            return player.isBot ? (BotKeyFlag | player.NetworkObjectId) : player.OwnerClientId;
        }

        /// <summary>Create or refresh a player's line (name, team, class) without changing counts.</summary>
        public void Touch(Player.ConquestPlayer player)
        {
            if (player == null) return;
            ulong key = KeyFor(player);
            m_Rows.TryGetValue(key, out var row);
            row.playerName = player.playerName.Value;
            row.clientId = player.isBot ? ulong.MaxValue : player.OwnerClientId;
            row.team = player.CurrentTeam;
            row.classIndex = player.CurrentClassIndex;
            row.isBot = player.isBot;
            Store(key, row);
        }

        // Writes a row, and its replicated copy only when something changed (no needless traffic).
        private void Store(ulong key, MatchResultRow row)
        {
            if (m_Rows.TryGetValue(key, out var existing))
            {
                if (existing.Equals(row)) return;
                m_Rows[key] = row;
                if (m_Mirror != null) m_Mirror[m_Order.IndexOf(key)] = row;
            }
            else
            {
                m_Rows[key] = row;
                m_Order.Add(key);
                if (m_Mirror != null) m_Mirror.Add(row);
            }
        }

        public void RecordDeath(Player.ConquestPlayer victim, Player.ConquestPlayer killer)
        {
            if (victim == null) return;
            Touch(victim);
            ulong victimKey = KeyFor(victim);
            var v = m_Rows[victimKey];
            v.deaths++;
            Store(victimKey, v);

            if (killer != null && killer != victim)
            {
                Touch(killer);
                ulong killerKey = KeyFor(killer);
                var k = m_Rows[killerKey];
                k.kills++;
                Store(killerKey, k);
            }
        }

        public void RecordCapture(Player.ConquestPlayer player)
        {
            if (player == null) return;
            Touch(player);
            ulong key = KeyFor(player);
            var row = m_Rows[key];
            row.captures++;
            Store(key, row);
        }

        public void CopyRowsTo(List<MatchResultRow> destination)
        {
            destination.Clear();
            foreach (var key in m_Order)
            {
                destination.Add(m_Rows[key]);
            }
        }
    }
}
