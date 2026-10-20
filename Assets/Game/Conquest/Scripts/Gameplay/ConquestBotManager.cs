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

        [Header("Population Settings")]
        [Tooltip("Target participants (human + bots) per team. Default 4v4 (8 total).")]
        [SerializeField] private int targetParticipantsPerTeam = 4;

        [Tooltip("If true, bots will automatically spawn to fill remaining team slots.")]
        [SerializeField] private bool enableBotFilling = true;

        [Tooltip("Bot difficulty preset (Easy, Normal, Hard).")]
        [SerializeField] private BotDifficulty botDifficulty = BotDifficulty.Easy;

        [Header("Bot Prefab & Timing")]
        [SerializeField] private GameObject botPrefab;
        [SerializeField] private float populationCheckInterval = 2.0f;

        private readonly List<ConquestBotController> m_ActiveBots = new List<ConquestBotController>();
        private float m_NextCheckTime;
        private int m_AssignedClassCounter = 0;

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
                    targetParticipantsPerTeam = ConquestSessionManager.Instance.targetTeamSize;
                    enableBotFilling = ConquestSessionManager.Instance.enableBotFilling;
                    botDifficulty = ConquestSessionManager.Instance.botDifficulty;
                }

                m_NextCheckTime = Time.time + 1.0f;
            }
        }

        private void Update()
        {
            if (!IsServer || !enableBotFilling) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !NetworkManager.Singleton.IsListening) return;

            // Stop bot spawning if match is over
            if (ConquestMatchManager.Instance != null && ConquestMatchManager.Instance.IsMatchOver) return;

            if (Time.time >= m_NextCheckTime)
            {
                m_NextCheckTime = Time.time + populationCheckInterval;
                MaintainTeamPopulations();
            }
        }

        private void MaintainTeamPopulations()
        {
            if (botPrefab == null) return;

            int redHumans = 0;
            int blueHumans = 0;
            int redBots = 0;
            int blueBots = 0;

            // Count existing participants
            foreach (var p in FindObjectsByType<ConquestPlayer>(FindObjectsInactive.Exclude))
            {
                if (p.isBot)
                {
                    if (p.CurrentTeam == Team.Red) redBots++;
                    else if (p.CurrentTeam == Team.Blue) blueBots++;
                }
                else
                {
                    if (p.CurrentTeam == Team.Red) redHumans++;
                    else if (p.CurrentTeam == Team.Blue) blueHumans++;
                }
            }

            int redTotal = redHumans + redBots;
            int blueTotal = blueHumans + blueBots;

            // Fill Red team
            while (redTotal < targetParticipantsPerTeam)
            {
                SpawnBot(Team.Red);
                redTotal++;
            }

            // Fill Blue team
            while (blueTotal < targetParticipantsPerTeam)
            {
                SpawnBot(Team.Blue);
                blueTotal++;
            }
        }

        public bool ShouldRetireBot(Team team)
        {
            int count = 0;
            foreach (var p in FindObjectsByType<ConquestPlayer>(FindObjectsInactive.Exclude))
            {
                if (p.CurrentTeam == team) count++;
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
                botPlayer.playerName.Value = $"Bot_{team}_{Random.Range(10, 99)}";
                botPlayer.isBot = true;

                // Cycle assigned class (0=Assault, 1=Heavy, 2=Engineer, 3=Recon) for balanced class spread
                int classIdx = (m_AssignedClassCounter++) % 4;
                botPlayer.ServerSetClass(classIdx);
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
