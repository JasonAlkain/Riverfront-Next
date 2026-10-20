using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Objectives;
using Game.Conquest.Player;
using Game.Conquest.Networking;

namespace Game.Conquest.Gameplay
{
    public class ConquestMatchManager : NetworkBehaviour
    {
        public static ConquestMatchManager Instance { get; private set; }

        [Header("Match Settings")]
        [Header("All Posts Win")]
        [Tooltip("Seconds a team must hold every command post to win. 0 wins the moment the last post falls.")]
        [SerializeField] private float allPostsHoldSeconds = 20f;

        [Header("Command Posts")]
        [SerializeField] private List<ConquestCommandPost> commandPosts = new List<ConquestCommandPost>();

        // Networked match state
        public NetworkVariable<int> redTickets = new NetworkVariable<int>(
            250,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<int> blueTickets = new NetworkVariable<int>(
            250,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> isMatchOver = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<Team> winningTeam = new NetworkVariable<Team>(
            Team.Neutral,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>The team holding every command post, or Neutral when no team does.</summary>
        public NetworkVariable<Team> allPostsHolder = new NetworkVariable<Team>(
            Team.Neutral,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>Server time at which <see cref="allPostsHolder"/> wins if it still holds every post.</summary>
        public NetworkVariable<double> allPostsWinAt = new NetworkVariable<double>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private float m_MatchStartTime;
        private readonly MatchStatsTracker m_Stats = new MatchStatsTracker();
        private static int s_MatchCounter;

        public int RedTickets => redTickets.Value;
        public int BlueTickets => blueTickets.Value;
        public bool IsMatchOver => isMatchOver.Value;
        public Team WinningTeam => winningTeam.Value;
        public IReadOnlyList<ConquestCommandPost> CommandPosts => commandPosts;
        public Team AllPostsHolder => allPostsHolder.Value;
        /// <summary>Seconds until the all-posts holder wins; 0 when no team holds every post.</summary>
        public float AllPostsSecondsLeft =>
            allPostsHolder.Value == Team.Neutral || NetworkManager == null
                ? 0f
                : Mathf.Max(0f, (float)(allPostsWinAt.Value - NetworkManager.ServerTime.Time));

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        [Header("Fall Limit")]
        [SerializeField] private float fallLimitY = -25.0f;

        public override void OnNetworkSpawn()
        {
            Game.Conquest.Scenes.ConquestSceneTransitionManager.Instance?.HideLoading();

            // Every peer fills the list: the HUD reads it on clients too, and an empty
            // serialized list used to leave clients with no command posts or spawn buttons.
            FindAllCommandPosts();

            // A new match: forget the previous results on every peer.
            ConquestSessionManager.Instance?.ClearMatchResults();

            if (IsServer)
            {
                m_MatchStartTime = Time.time;
                // Tickets are only spent on deaths (SWBF2-style); holding posts doesn't bleed the enemy.
                var settings = ConquestSessionManager.Instance != null
                    ? ConquestSessionManager.Instance.matchSettings
                    : ConquestMatchSettings.Default;
                redTickets.Value = settings.reinforcementTickets;
                blueTickets.Value = settings.reinforcementTickets;
                isMatchOver.Value = false;
                winningTeam.Value = Team.Neutral;
                allPostsHolder.Value = Team.Neutral;
                allPostsWinAt.Value = 0;
            }
        }

        public void FindAllCommandPosts()
        {
            if (commandPosts.Count == 0)
            {
                commandPosts.AddRange(FindObjectsByType<ConquestCommandPost>(FindObjectsInactive.Exclude));
            }
        }

        private void Update()
        {
            if (!IsServer || isMatchOver.Value) return;

            EvaluateFallLimits();
            EvaluateAllPostsHeld();
        }

        // Second win condition: one team holds every post for allPostsHoldSeconds.
        // Losing a single post (even to neutral) cancels the countdown.
        private void EvaluateAllPostsHeld()
        {
            Team holder = TeamHoldingAllPosts();
            double now = NetworkManager.ServerTime.Time;

            if (holder != allPostsHolder.Value)
            {
                allPostsHolder.Value = holder;
                allPostsWinAt.Value = holder == Team.Neutral ? 0 : now + allPostsHoldSeconds;
            }

            if (holder != Team.Neutral && now >= allPostsWinAt.Value)
            {
                EndMatch(holder);
            }
        }

        private Team TeamHoldingAllPosts()
        {
            Team holder = Team.Neutral;
            foreach (var post in commandPosts)
            {
                if (post == null) continue;

                Team owner = post.CurrentOwner;
                if (owner == Team.Neutral) return Team.Neutral;
                if (holder == Team.Neutral) holder = owner;
                else if (owner != holder) return Team.Neutral;
            }
            return holder;
        }

        private void EvaluateFallLimits()
        {
            var players = FindObjectsByType<ConquestPlayer>(FindObjectsInactive.Exclude);
            foreach (var p in players)
            {
                if (p != null && p.transform.position.y < fallLimitY)
                {
                    if (p.Health != null && !p.Health.IsDead)
                    {
                        p.Health.TakeDamage(p.Health.MaxHealth * 10, Game.Conquest.Combat.ConquestHealth.NoAttacker);
                    }
                }
            }
        }

        /// <param name="killerObjectId">Attacker's NetworkObjectId, or ConquestHealth.NoAttacker.</param>
        public void OnPlayerDied(ConquestPlayer victim, ulong killerObjectId)
        {
            if (!IsServer || isMatchOver.Value) return;

            ConquestPlayer killer = null;
            if (killerObjectId != Game.Conquest.Combat.ConquestHealth.NoAttacker &&
                NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(killerObjectId, out var killerObject))
            {
                killer = killerObject.GetComponent<ConquestPlayer>();
            }
            m_Stats.RecordDeath(victim, killer);

            // Death costs 1 ticket to victim's team
            if (victim.CurrentTeam == Team.Red)
            {
                DrainTickets(Team.Red, 1);
            }
            else if (victim.CurrentTeam == Team.Blue)
            {
                DrainTickets(Team.Blue, 1);
            }
        }

        public void DrainTickets(Team team, int amount)
        {
            if (!IsServer || isMatchOver.Value) return;

            if (team == Team.Red)
            {
                redTickets.Value = Mathf.Max(0, redTickets.Value - amount);
                if (redTickets.Value <= 0)
                {
                    EndMatch(Team.Blue);
                }
            }
            else if (team == Team.Blue)
            {
                blueTickets.Value = Mathf.Max(0, blueTickets.Value - amount);
                if (blueTickets.Value <= 0)
                {
                    EndMatch(Team.Red);
                }
            }
        }

        private void EndMatch(Team winner)
        {
            isMatchOver.Value = true;
            winningTeam.Value = winner;
            Debug.Log($"[ConquestMatchManager] MATCH OVER! Winning team: {winner}");

            var session = ConquestSessionManager.Instance;
            session?.SetMatchResults(BuildResults(winner));
            StartCoroutine(ShowResultsAfterBanner());
        }

        private MatchResults BuildResults(Team winner)
        {
            // Make sure everyone still in the match has a line, even with no kills or deaths.
            foreach (var p in FindObjectsByType<ConquestPlayer>(FindObjectsInactive.Exclude))
            {
                if (p != null && p.IsSpawned) m_Stats.Touch(p);
            }

            var session = ConquestSessionManager.Instance;
            var results = new MatchResults
            {
                matchId = ++s_MatchCounter,
                winner = winner,
                redTickets = redTickets.Value,
                blueTickets = blueTickets.Value,
                durationSeconds = Time.time - m_MatchStartTime,
                mapScene = gameObject.scene.name,
                returnToLobbySeconds = session != null ? session.resultsScreenSeconds : 15f,
            };
            m_Stats.CopyRowsTo(results.rows);
            return results;
        }

        // Server: leave the VICTORY/DEFEAT banner up, then take everyone to the results scene.
        private IEnumerator ShowResultsAfterBanner()
        {
            var session = ConquestSessionManager.Instance;
            yield return new WaitForSeconds(session != null ? session.victoryBannerSeconds : 6f);

            // Players, bots and stations are spawned with destroyWithScene = false, so they would
            // follow everyone into the results scene and lobby. Players are respawned on the next map.
            var spawned = new List<NetworkObject>(NetworkManager.SpawnManager.SpawnedObjectsList);
            foreach (var networkObject in spawned)
            {
                if (networkObject != null && networkObject.IsSpawned && networkObject.IsSceneObject != true)
                {
                    networkObject.Despawn(true);
                }
            }

            string resultsScene = session != null ? session.resultsSceneName : "MatchResults";
            Game.Conquest.Scenes.ConquestSceneTransitionManager.Instance?.TransitionToMap(resultsScene, true);
        }

        public bool GetFriendlyPostSpawnPoint(Team team, string preferredPostName, out Vector3 position, out Quaternion rotation)
        {
            // 1. Try preferred post
            if (!string.IsNullOrEmpty(preferredPostName))
            {
                foreach (var post in commandPosts)
                {
                    if (IsSpawnablePost(post, team) && post.PostName == preferredPostName)
                    {
                        return post.GetSpawnPoint(out position, out rotation);
                    }
                }
            }

            // 2. Pick any friendly post
            List<ConquestCommandPost> friendlyPosts = new List<ConquestCommandPost>();
            foreach (var post in commandPosts)
            {
                if (IsSpawnablePost(post, team))
                {
                    friendlyPosts.Add(post);
                }
            }

            if (friendlyPosts.Count > 0)
            {
                var chosen = friendlyPosts[UnityEngine.Random.Range(0, friendlyPosts.Count)];
                return chosen.GetSpawnPoint(out position, out rotation);
            }

            // No usable post: the caller falls back to the team's base spawn points.
            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        // Owned by the team and with no enemies in the zone, so nobody spawns into a fight.
        private static bool IsSpawnablePost(ConquestCommandPost post, Team team)
        {
            return post != null && post.CurrentOwner == team && !post.IsSpawnBlocked;
        }

        public List<string> GetFriendlyPostNames(Team team)
        {
            List<string> list = new List<string>();
            foreach (var post in commandPosts)
            {
                if (IsSpawnablePost(post, team))
                {
                    list.Add(post.PostName);
                }
            }
            return list;
        }
    }
}
