using System;
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
        [SerializeField] private int initialTickets = 250;
        [SerializeField] private float ticketDrainInterval = 3.0f; // Seconds between majority drains
        [SerializeField] private int ticketDrainAmount = 1;

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

        private float m_NextDrainTime;

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

            if (IsServer)
            {
                if (ConquestSessionManager.Instance != null)
                {
                    initialTickets = ConquestSessionManager.Instance.reinforcementTickets;
                }

                redTickets.Value = initialTickets;
                blueTickets.Value = initialTickets;
                isMatchOver.Value = false;
                winningTeam.Value = Team.Neutral;
                m_NextDrainTime = Time.time + ticketDrainInterval;

                FindAllCommandPosts();
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

            // Handle majority post ticket drain
            if (Time.time >= m_NextDrainTime)
            {
                m_NextDrainTime = Time.time + ticketDrainInterval;
                EvaluateMajorityPostDrain();
            }
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
                        p.Health.TakeDamage(p.Health.MaxHealth * 10, 0);
                    }
                }
            }
        }

        private void EvaluateMajorityPostDrain()
        {
            int redPosts = 0;
            int bluePosts = 0;

            foreach (var post in commandPosts)
            {
                if (post != null)
                {
                    if (post.CurrentOwner == Team.Red) redPosts++;
                    else if (post.CurrentOwner == Team.Blue) bluePosts++;
                }
            }

            // Majority of 5 posts is 3 or more
            if (redPosts > bluePosts)
            {
                DrainTickets(Team.Blue, ticketDrainAmount);
            }
            else if (bluePosts > redPosts)
            {
                DrainTickets(Team.Red, ticketDrainAmount);
            }
        }

        public void OnPlayerDied(ConquestPlayer victim)
        {
            if (!IsServer || isMatchOver.Value) return;

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
