using System;
using System.Collections.Generic;
using UnityEngine;
using Game.Conquest.Objectives;
using Game.Conquest.Player;

namespace Game.Conquest.Gameplay
{
    /// <summary>
    /// Server: hands every bot of a team a command post and a role (attack or defend), so a team
    /// spreads over the map instead of every bot chasing the same nearest post.
    /// Defence comes first: posts under attack, then posts with enemies close by, each get a few
    /// of the nearest bots. Everyone else attacks, spread over the posts the team doesn't hold.
    /// All numbers here are placeholders until bot matches have been play-tested.
    /// </summary>
    [Serializable]
    public class ConquestBotObjectivePlanner
    {
        [Tooltip("Placeholder: bots sent to an owned post that enemies are standing on.")]
        [SerializeField] private int respondersPerAttackedPost = 3;
        [Tooltip("Placeholder: bots kept on an owned post with enemies near it but not on it yet.")]
        [SerializeField] private int guardsPerThreatenedPost = 1;
        [Tooltip("Placeholder: metres around a post in which an enemy counts as a threat to it.")]
        [SerializeField] private float threatRadius = 30f;
        [Tooltip("Placeholder: share of the team that may be on defence while there are posts left to take.")]
        [Range(0f, 1f)]
        [SerializeField] private float maxDefenderShare = 0.5f;
        [Tooltip("Placeholder: metres added to a post's distance for each teammate already sent to attack it.")]
        [SerializeField] private float attackSpreadPenalty = 40f;
        [Tooltip("Placeholder: metres taken off a post's distance for a neutral post, which is quicker to take.")]
        [SerializeField] private float neutralPostBonus = 15f;
        [Tooltip("Placeholder: metres taken off the distance to a bot's current post, so it doesn't flip between two equal choices.")]
        [SerializeField] private float stickiness = 20f;

        private readonly List<ConquestBotController> m_Unassigned = new List<ConquestBotController>();
        private readonly List<ConquestCommandPost> m_AttackTargets = new List<ConquestCommandPost>();
        private readonly List<int> m_AttackCounts = new List<int>();

        public float ThreatRadius => threatRadius;

        /// <summary>Assign every living bot in <paramref name="bots"/> that plays for <paramref name="team"/>.</summary>
        public void AssignTeam(Team team, IReadOnlyList<ConquestBotController> bots, IReadOnlyList<ConquestCommandPost> posts)
        {
            m_Unassigned.Clear();
            for (int i = 0; i < bots.Count; i++)
            {
                ConquestBotController bot = bots[i];
                if (bot != null && bot.IsAvailableForOrders && bot.Team == team) m_Unassigned.Add(bot);
            }
            if (m_Unassigned.Count == 0) return;

            if (posts == null || posts.Count == 0)
            {
                for (int i = 0; i < m_Unassigned.Count; i++) m_Unassigned[i].AssignObjective(null, false);
                return;
            }

            m_AttackTargets.Clear();
            for (int i = 0; i < posts.Count; i++)
            {
                // Home bases can't be taken, so nobody is sent to attack one.
                if (posts[i] != null && posts[i].IsCapturable && posts[i].CurrentOwner != team) m_AttackTargets.Add(posts[i]);
            }

            // With nothing left to take, the whole team may defend.
            int defenderBudget = m_AttackTargets.Count == 0
                ? m_Unassigned.Count
                : Mathf.FloorToInt(m_Unassigned.Count * maxDefenderShare);

            // Posts being taken right now first, then posts with enemies closing in.
            for (int i = 0; i < posts.Count && defenderBudget > 0; i++)
            {
                if (IsUnderAttack(posts[i], team)) defenderBudget -= SendNearest(posts[i], respondersPerAttackedPost, defenderBudget);
            }
            for (int i = 0; i < posts.Count && defenderBudget > 0; i++)
            {
                ConquestCommandPost post = posts[i];
                if (post == null || !post.IsCapturable || post.CurrentOwner != team || IsUnderAttack(post, team)) continue;
                if (CountEnemiesNear(post.transform.position, team) > 0)
                {
                    defenderBudget -= SendNearest(post, guardsPerThreatenedPost, defenderBudget);
                }
            }

            if (m_AttackTargets.Count > 0) AssignAttackers();
            else SpreadOverOwnedPosts(team, posts);
        }

        /// <summary>An owned post with enemies on it, or whose capture is sliding away from the owner.</summary>
        public static bool IsUnderAttack(ConquestCommandPost post, Team team)
        {
            if (post == null || !post.IsCapturable || post.CurrentOwner != team) return false;
            return post.IsSpawnBlocked || (post.CaptureProgress < 0.99f && post.capturingTeam.Value != team);
        }

        public int CountEnemiesNear(Vector3 position, Team team)
        {
            int count = 0;
            float sqrRadius = threatRadius * threatRadius;
            var all = ConquestPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                ConquestPlayer p = all[i];
                if (p == null || p.Health.IsDead || p.CurrentTeam == team || p.CurrentTeam == Team.Neutral) continue;
                if ((p.transform.position - position).sqrMagnitude <= sqrRadius) count++;
            }
            return count;
        }

        // Sends up to `wanted` of the nearest unassigned bots to defend the post. Returns how many went.
        private int SendNearest(ConquestCommandPost post, int wanted, int budget)
        {
            int sent = 0;
            int limit = Mathf.Min(wanted, budget);
            while (sent < limit && m_Unassigned.Count > 0)
            {
                int best = -1;
                float bestCost = float.MaxValue;
                for (int i = 0; i < m_Unassigned.Count; i++)
                {
                    float cost = Cost(m_Unassigned[i], post) - m_Unassigned[i].DefendPreference;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        best = i;
                    }
                }
                m_Unassigned[best].AssignObjective(post, true);
                m_Unassigned.RemoveAt(best);
                sent++;
            }
            return sent;
        }

        // Each attacker takes the post that is cheapest for it, where every teammate already
        // heading there makes a post more expensive. That spreads a team over two or more posts.
        private void AssignAttackers()
        {
            m_AttackCounts.Clear();
            for (int i = 0; i < m_AttackTargets.Count; i++) m_AttackCounts.Add(0);

            for (int b = 0; b < m_Unassigned.Count; b++)
            {
                ConquestBotController bot = m_Unassigned[b];
                int best = 0;
                float bestCost = float.MaxValue;
                for (int i = 0; i < m_AttackTargets.Count; i++)
                {
                    ConquestCommandPost post = m_AttackTargets[i];
                    float cost = Cost(bot, post) + m_AttackCounts[i] * attackSpreadPenalty;
                    if (post.CurrentOwner == Team.Neutral) cost -= neutralPostBonus;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        best = i;
                    }
                }
                m_AttackCounts[best]++;
                bot.AssignObjective(m_AttackTargets[best], false);
            }
            m_Unassigned.Clear();
        }

        // The team holds every post: share the rest out evenly, nearest first.
        private void SpreadOverOwnedPosts(Team team, IReadOnlyList<ConquestCommandPost> posts)
        {
            m_AttackCounts.Clear();
            for (int i = 0; i < posts.Count; i++) m_AttackCounts.Add(0);

            for (int b = 0; b < m_Unassigned.Count; b++)
            {
                ConquestBotController bot = m_Unassigned[b];
                int best = -1;
                float bestCost = float.MaxValue;
                for (int i = 0; i < posts.Count; i++)
                {
                    if (posts[i] == null || !posts[i].IsCapturable || posts[i].CurrentOwner != team) continue;
                    float cost = Cost(bot, posts[i]) + m_AttackCounts[i] * attackSpreadPenalty;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        best = i;
                    }
                }
                if (best < 0)
                {
                    bot.AssignObjective(null, false);
                    continue;
                }
                m_AttackCounts[best]++;
                bot.AssignObjective(posts[best], true);
            }
            m_Unassigned.Clear();
        }

        private float Cost(ConquestBotController bot, ConquestCommandPost post)
        {
            float cost = Vector3.Distance(bot.transform.position, post.transform.position);
            if (bot.TargetPost == post) cost -= stickiness;
            return cost;
        }
    }
}
