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
        // Tickets are only spent on deaths (SWBF2-style); holding posts doesn't bleed the enemy.
        [SerializeField] private int initialTickets = 250;

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

        private float m_MatchStartTime;
        private readonly MatchStatsTracker m_Stats = new MatchStatsTracker();
        private static int s_MatchCounter;

        public int RedTickets => redTickets.Value;
        public int BlueTickets => blueTickets.Value;
        public bool IsMatchOver => isMatchOver.Value;
        public Team WinningTeam => winningTeam.Value;
        public IReadOnlyList<ConquestCommandPost> CommandPosts => commandPosts;

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
                if (ConquestSessionManager.Instance != null)
                {
                    initialTickets = ConquestSessionManager.Instance.reinforcementTickets;
                }

                redTickets.Value = initialTickets;
                blueTickets.Value = initialTickets;
                isMatchOver.Value = false;
                winningTeam.Value = Team.Neutral;
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
                    if (post != null && post.PostName == preferredPostName && post.CurrentOwner == team)
                    {
                        return post.GetSpawnPoint(out position, out rotation);
                    }
                }
            }

            // 2. Pick any friendly post
            List<ConquestCommandPost> friendlyPosts = new List<ConquestCommandPost>();
            foreach (var post in commandPosts)
            {
                if (post != null && post.CurrentOwner == team)
                {
                    friendlyPosts.Add(post);
                }
            }

            if (friendlyPosts.Count > 0)
            {
                var chosen = friendlyPosts[UnityEngine.Random.Range(0, friendlyPosts.Count)];
                return chosen.GetSpawnPoint(out position, out rotation);
            }

            // Fallback default
            position = team == Team.Red ? new Vector3(-20f, 1f, 0f) : new Vector3(20f, 1f, 0f);
            rotation = Quaternion.identity;
            return false;
        }

        public List<string> GetFriendlyPostNames(Team team)
        {
            List<string> list = new List<string>();
            foreach (var post in commandPosts)
            {
                if (post != null && post.CurrentOwner == team)
                {
                    list.Add(post.PostName);
                }
            }
            return list;
        }
    }
}
