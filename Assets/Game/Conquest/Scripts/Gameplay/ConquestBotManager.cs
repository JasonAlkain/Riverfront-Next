using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Player;
using Game.Conquest.Networking;

namespace Game.Conquest.Gameplay
{
    public class ConquestBotManager : NetworkBehaviour
    {
        public static ConquestBotManager Instance { get; private set; }

        // Population comes from the session's match settings when the match spawns.
        private int targetParticipantsPerTeam = ConquestMatchSettings.Default.teamSize;
        private bool enableBotFilling = ConquestMatchSettings.Default.botFilling;
        private BotDifficulty botDifficulty = ConquestMatchSettings.Default.botDifficulty;

        [Header("Bot Prefab & Timing")]
        [SerializeField] private GameObject botPrefab;
        [SerializeField] private float populationCheckInterval = 2.0f;
        [Tooltip("Placeholder: seconds between two bot spawns while teams are filling, so a full team doesn't arrive in one frame.")]
        [SerializeField] private float spawnStaggerSeconds = 0.25f;

        [Header("Objectives")]
        [Tooltip("Placeholder: seconds between objective assignments. A bot that finishes its post asks for one sooner.")]
        [SerializeField] private float objectiveAssignInterval = 1.5f;
        [SerializeField] private ConquestBotObjectivePlanner objectivePlanner = new ConquestBotObjectivePlanner();

        private readonly List<ConquestBotController> m_ActiveBots = new List<ConquestBotController>();
        private float m_NextCheckTime;
        private float m_NextAssignTime;
        private float m_LastAssignTime;
        private const float MinReassignGap = 0.25f;

        // Bot names count up for the whole match, so two bots never share one.
        private int m_NextBotNumber = 1;
        private readonly int[] m_ClassCounts = new int[8];

        public ConquestBotObjectivePlanner ObjectivePlanner => objectivePlanner;

        public int TargetParticipantsPerTeam
        {
            get => targetParticipantsPerTeam;
            set => targetParticipantsPerTeam = Mathf.Max(1, value);
        }

        public bool EnableBotFilling
        {
            get => enableBotFilling;
            set => enableBotFilling = value;
        }

        public BotDifficulty Difficulty
        {
            get => botDifficulty;
            set
            {
                botDifficulty = value;
                foreach (var b in m_ActiveBots)
                {
                    if (b != null) b.SetDifficulty(value);
                }
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                if (ConquestSessionManager.Instance != null)
                {
                    var settings = ConquestSessionManager.Instance.matchSettings;
                    targetParticipantsPerTeam = Mathf.Max(1, settings.teamSize);
                    enableBotFilling = settings.botFilling;
                    botDifficulty = settings.botDifficulty;
                }

                m_NextCheckTime = Time.time + 1.0f;
            }
        }

        private void Update()
        {
            if (!IsServer) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !NetworkManager.Singleton.IsListening) return;

            // Stop bot spawning if match is over
            var match = ConquestMatchManager.Instance;
            if (match != null && match.IsMatchOver) return;

            if (enableBotFilling && Time.time >= m_NextCheckTime)
            {
                // Short wait after a spawn while a team is still short, the normal check interval otherwise.
                bool spawned = SpawnOneMissingBot();
                m_NextCheckTime = Time.time + (spawned ? spawnStaggerSeconds : populationCheckInterval);
            }

            if (match != null && Time.time >= m_NextAssignTime)
            {
                m_LastAssignTime = Time.time;
                m_NextAssignTime = Time.time + objectiveAssignInterval;
                objectivePlanner.AssignTeam(Team.Red, m_ActiveBots, match.CommandPosts);
                objectivePlanner.AssignTeam(Team.Blue, m_ActiveBots, match.CommandPosts);
            }
        }

        /// <summary>Server: a bot has finished its post (or has none); hand out objectives on the next frame.</summary>
        public void RequestReassignment()
        {
            // Several bots finishing together still cause only one pass.
            m_NextAssignTime = Mathf.Min(m_NextAssignTime, m_LastAssignTime + MinReassignGap);
        }

        /// <summary>Server: if a team is below its size, spawn one bot for the team that is shortest. Returns true if it spawned one.</summary>
        private bool SpawnOneMissingBot()
        {
            if (botPrefab == null) return false;
            for (int i = m_ActiveBots.Count - 1; i >= 0; i--)
            {
                if (m_ActiveBots[i] == null || !m_ActiveBots[i].IsSpawned) m_ActiveBots.RemoveAt(i);
            }

            int red = 0;
            int blue = 0;
            foreach (var p in ConquestPlayer.All)
            {
                if (p == null) continue;
                if (p.CurrentTeam == Team.Red) red++;
                else if (p.CurrentTeam == Team.Blue) blue++;
            }

            int redMissing = targetParticipantsPerTeam - red;
            int blueMissing = targetParticipantsPerTeam - blue;
            if (redMissing <= 0 && blueMissing <= 0) return false;

            // The shorter team first, so both teams fill side by side and neither sits empty.
            SpawnBot(redMissing >= blueMissing ? Team.Red : Team.Blue);
            return true;
        }

        /// <summary>Server: a bot left the match (retired or the match ended).</summary>
        public void Unregister(ConquestBotController bot)
        {
            m_ActiveBots.Remove(bot);
        }

        // The class the team has fewest of, humans included, so each team gets a balanced mix.
        private int PickClassForTeam(Team team, ConquestPlayer newBot, int classCount)
        {
            classCount = Mathf.Clamp(classCount, 1, m_ClassCounts.Length);
            System.Array.Clear(m_ClassCounts, 0, m_ClassCounts.Length);
            foreach (var p in ConquestPlayer.All)
            {
                if (p == null || p == newBot || p.CurrentTeam != team) continue;
                int index = p.CurrentClassIndex;
                if (index >= 0 && index < classCount) m_ClassCounts[index]++;
            }

            int best = 0;
            for (int i = 1; i < classCount; i++)
            {
                if (m_ClassCounts[i] < m_ClassCounts[best]) best = i;
            }
            return best;
        }

        public bool ShouldRetireBot(Team team)
        {
            int count = 0;
            foreach (var p in ConquestPlayer.All)
            {
                if (p != null && p.CurrentTeam == team) count++;
            }
            return count > targetParticipantsPerTeam;
        }

        private void SpawnBot(Team team)
        {
            Vector3 spawnPos = Vector3.zero;
            Quaternion spawnRot = Quaternion.identity;

            bool foundSpawn = false;
            if (ConquestMatchManager.Instance != null)
            {
                foundSpawn = ConquestMatchManager.Instance.GetFriendlyPostSpawnPoint(team, "", out spawnPos, out spawnRot);
            }
            if (!foundSpawn && ConquestNetworkManager.Instance != null)
            {
                ConquestNetworkManager.Instance.GetSpawnPointForTeam(team, out spawnPos, out spawnRot);
            }

            GameObject botObj = Instantiate(botPrefab, spawnPos, spawnRot);
            var netObj = botObj.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                netObj.Spawn();
            }

            var botPlayer = botObj.GetComponent<ConquestPlayer>();
            var botController = botObj.GetComponent<ConquestBotController>();

            if (botPlayer != null)
            {
                botPlayer.playerTeam.Value = team;
                botPlayer.playerName.Value = $"Bot_{team}_{m_NextBotNumber++:00}";
                botPlayer.isBot = true;
                botPlayer.ServerSetClass(PickClassForTeam(team, botPlayer, botPlayer.AvailableClasses.Count));
                botPlayer.ApplyTeamVisuals(team);
            }

            if (botController != null)
            {
                botController.SetDifficulty(botDifficulty);
                m_ActiveBots.Add(botController);
            }

            Debug.Log($"[ConquestBotManager] Spawned {botDifficulty} {team} Bot (Class: {botPlayer?.selectedClassIndex.Value}) at {spawnPos}");
        }
    }
}
