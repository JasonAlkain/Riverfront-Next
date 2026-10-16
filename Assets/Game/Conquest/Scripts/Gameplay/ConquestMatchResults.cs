using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

namespace Game.Conquest.Gameplay
{
    /// <summary>One combatant's line on the results screen.</summary>
    public struct MatchResultRow : INetworkSerializable
    {
        public FixedString64Bytes playerName;
        /// <summary>Owning client for humans; ulong.MaxValue for bots.</summary>
        public ulong clientId;
        public Team team;
        public int classIndex;
        public bool isBot;
        public int kills;
        public int deaths;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref playerName);
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref team);
            serializer.SerializeValue(ref classIndex);
            serializer.SerializeValue(ref isBot);
            serializer.SerializeValue(ref kills);
            serializer.SerializeValue(ref deaths);
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
            if (!m_Rows.TryGetValue(key, out var row))
            {
                m_Order.Add(key);
                row = new MatchResultRow();
            }
            row.playerName = player.playerName.Value;
            row.clientId = player.isBot ? ulong.MaxValue : player.OwnerClientId;
            row.team = player.CurrentTeam;
            row.classIndex = player.CurrentClassIndex;
            row.isBot = player.isBot;
            m_Rows[key] = row;
        }

        public void RecordDeath(Player.ConquestPlayer victim, Player.ConquestPlayer killer)
        {
            if (victim == null) return;
            Touch(victim);
            ulong victimKey = KeyFor(victim);
            var v = m_Rows[victimKey];
            v.deaths++;
            m_Rows[victimKey] = v;

            if (killer != null && killer != victim)
            {
                Touch(killer);
                ulong killerKey = KeyFor(killer);
                var k = m_Rows[killerKey];
                k.kills++;
                m_Rows[killerKey] = k;
            }
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
