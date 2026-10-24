using System;
using UnityEngine;
using UnityEngine.Serialization;
using Game.Conquest.Player;

namespace Game.Conquest.Gameplay
{
    /// <summary>Conquest tuning, set on the match manager in each map scene.</summary>
    [Serializable]
    public class ConquestModeRules
    {
        [Tooltip("Placeholder: seconds a team must hold every command post to win. 0 wins the moment the last post falls.")]
        public float allPostsHoldSeconds = 20f;
        [Tooltip("Placeholder: tickets a team spends each time one of its players or bots respawns. " +
                 "A team without enough can't respawn. A human's first deploy into a match is free.")]
        [FormerlySerializedAs("ticketsPerDeath")]
        [Min(0)] public int ticketsPerRespawn = 1;
        [Tooltip("Placeholder: seconds after a death before a player may deploy again (bots respawn after the same wait).")]
        [Min(0f)] public float respawnSeconds = 1f;
    }

    /// <summary>
    /// Conquest: both teams start with the match's reinforcement tickets and spend them to respawn
    /// (holding posts doesn't bleed the enemy). A team that can't afford a respawn stays dead, and
    /// human players get the last tickets before bots do. A team loses when it can't respawn and has
    /// nobody left alive, or when the other team holds every command post for
    /// <see cref="ConquestModeRules.allPostsHoldSeconds"/>. If both teams are wiped out on the same
    /// tick, the one with more tickets left wins, and equal tickets is a draw.
    /// </summary>
    public class ConquestMode : IConquestGameMode
    {
        private readonly ConquestModeRules m_Rules;

        public ConquestMode(ConquestModeRules rules)
        {
            m_Rules = rules;
        }

        public string Id => ConquestGameModes.ConquestId;

        public float RespawnSeconds => Mathf.Max(0f, m_Rules.respawnSeconds);

        public void ServerBegin(ConquestMatchManager match, in ConquestMatchSettings settings)
        {
            match.ServerSetTickets(settings.reinforcementTickets, settings.reinforcementTickets);
            match.ServerSetAllPostsCountdown(Team.Neutral, 0);
        }

        public int RespawnTicketCost => Mathf.Max(0, m_Rules.ticketsPerRespawn);

        public void ServerTick(ConquestMatchManager match)
        {
            EvaluateAllPostsHeld(match);
            if (!match.IsMatchOver) EvaluateEliminated(match);
        }

        public void ServerOnDeath(ConquestMatchManager match, ConquestPlayer victim, ConquestPlayer killer)
        {
            // Deaths cost nothing by themselves; the respawn that follows does.
        }

        public bool ServerTrySpendRespawn(ConquestMatchManager match, ConquestPlayer combatant)
        {
            Team team = combatant.CurrentTeam;
            int cost = RespawnTicketCost;
            if (team == Team.Neutral || cost == 0) return true;

            int available = match.TicketsFor(team);
            // Players come first: a bot only takes a ticket that no dead human on its team needs.
            if (combatant.isBot) available -= cost * CountDeadHumans(team);
            if (available < cost) return false;
            match.ServerSpendTickets(team, cost);
            return true;
        }

        private static int CountDeadHumans(Team team)
        {
            int count = 0;
            var all = ConquestPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                ConquestPlayer p = all[i];
                if (p != null && !p.isBot && p.CurrentTeam == team && p.Health != null && p.Health.IsDead) count++;
            }
            return count;
        }

        private static bool HasLivingMember(Team team)
        {
            var all = ConquestPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                ConquestPlayer p = all[i];
                if (p != null && p.CurrentTeam == team && p.Health != null && !p.Health.IsDead) return true;
            }
            return false;
        }

        // Out of reinforcements and nobody left standing: that team has lost.
        private void EvaluateEliminated(ConquestMatchManager match)
        {
            int cost = RespawnTicketCost;
            if (cost == 0) return;
            bool redOut = match.TicketsFor(Team.Red) < cost && !HasLivingMember(Team.Red);
            bool blueOut = match.TicketsFor(Team.Blue) < cost && !HasLivingMember(Team.Blue);
            if (!redOut && !blueOut) return;

            if (redOut && blueOut)
            {
                int red = match.TicketsFor(Team.Red), blue = match.TicketsFor(Team.Blue);
                match.ServerEndMatch(red > blue ? Team.Red : blue > red ? Team.Blue : Team.Neutral);
            }
            else
            {
                match.ServerEndMatch(redOut ? Team.Blue : Team.Red);
            }
        }

        // Second win condition: one team holds every capturable post for allPostsHoldSeconds.
        // Losing a single post (even to neutral) cancels the countdown.
        private void EvaluateAllPostsHeld(ConquestMatchManager match)
        {
            Team holder = match.TeamHoldingAllPosts();
            double now = match.NetworkManager.ServerTime.Time;

            if (holder != match.AllPostsHolder)
            {
                match.ServerSetAllPostsCountdown(holder, holder == Team.Neutral ? 0 : now + m_Rules.allPostsHoldSeconds);
            }

            if (holder != Team.Neutral && now >= match.allPostsWinAt.Value)
            {
                match.ServerEndMatch(holder);
            }
        }
    }
}
