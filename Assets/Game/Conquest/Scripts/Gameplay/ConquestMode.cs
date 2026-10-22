using System;
using UnityEngine;
using Game.Conquest.Player;

namespace Game.Conquest.Gameplay
{
    /// <summary>Conquest tuning, set on the match manager in each map scene.</summary>
    [Serializable]
    public class ConquestModeRules
    {
        [Tooltip("Placeholder: seconds a team must hold every command post to win. 0 wins the moment the last post falls.")]
        public float allPostsHoldSeconds = 20f;
        [Tooltip("Placeholder: tickets a team loses each time one of its players or bots dies.")]
        public int ticketsPerDeath = 1;
    }

    /// <summary>
    /// Conquest: both teams start with the match's reinforcement tickets and spend them on deaths
    /// (holding posts doesn't bleed the enemy). A team wins when the other runs out of tickets, or
    /// by holding every command post for <see cref="ConquestModeRules.allPostsHoldSeconds"/>.
    /// </summary>
    public class ConquestMode : IConquestGameMode
    {
        private readonly ConquestModeRules m_Rules;

        public ConquestMode(ConquestModeRules rules)
        {
            m_Rules = rules;
        }

        public string Id => ConquestGameModes.ConquestId;

        public void ServerBegin(ConquestMatchManager match, in ConquestMatchSettings settings)
        {
            match.ServerSetTickets(settings.reinforcementTickets, settings.reinforcementTickets);
            match.ServerSetAllPostsCountdown(Team.Neutral, 0);
        }

        public void ServerTick(ConquestMatchManager match)
        {
            EvaluateAllPostsHeld(match);
        }

        public void ServerOnDeath(ConquestMatchManager match, ConquestPlayer victim, ConquestPlayer killer)
        {
            if (victim.CurrentTeam != Team.Neutral) match.DrainTickets(victim.CurrentTeam, m_Rules.ticketsPerDeath);
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
