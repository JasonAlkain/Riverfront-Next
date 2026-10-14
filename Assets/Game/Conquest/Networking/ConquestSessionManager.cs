using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Game.Conquest.Gameplay;
using Game.Conquest.Maps;
using Game.Conquest.Scenes;

namespace Game.Conquest.Networking
{
    public class ConquestSessionManager : MonoBehaviour
    {
        public static ConquestSessionManager Instance { get; private set; }

        [Header("Active Session Configuration")]
        public string sessionName = "Conquest Session";
        public string selectedMapId = "battlefront_large";
        public int maxHumanPlayers = 16;
        public int reinforcementTickets = 150;
        public int targetTeamSize = 8;
        public bool enableBotFilling = true;
        public BotDifficulty botDifficulty = BotDifficulty.Normal;
        public bool isSoloMode = false;
        public string lastJoinCode = "";
        public string connectAddress = "127.0.0.1";
        public ushort connectPort = 7777;

        [Header("Services Status")]
        private bool m_ServicesInitialized = false;
        private string m_StatusMessage = "Ready";

        public bool ServicesInitialized => m_ServicesInitialized;
        public string StatusMessage => m_StatusMessage;

        public event Action<string> OnStatusChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private Task<bool> m_InitTask;

        private void Start()
        {
            _ = InitializeServicesAsync();
        }

        public Task<bool> InitializeServicesAsync()
        {
            if (m_ServicesInitialized) return Task.FromResult(true);
            if (m_InitTask != null && !m_InitTask.IsCompleted) return m_InitTask;

            m_InitTask = InternalInitializeAsync();
            return m_InitTask;
        }

        private async Task<bool> InternalInitializeAsync()
        {
            try
            {
                m_StatusMessage = "Initializing Gaming Services...";
                OnStatusChanged?.Invoke(m_StatusMessage);

                // Initialize Unity Services Core
                if (Unity.Services.Core.UnityServices.State == Unity.Services.Core.ServicesInitializationState.Uninitialized)
                {
                    await Unity.Services.Core.UnityServices.InitializeAsync();
                }

                // Initialize Authentication if not already signed in
                if (!Unity.Services.Authentication.AuthenticationService.Instance.IsSignedIn)
                {
                    await Unity.Services.Authentication.AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                m_ServicesInitialized = true;
                m_StatusMessage = "Online Services Ready";
                OnStatusChanged?.Invoke(m_StatusMessage);
                return true;
            }
            catch (Exception ex)
            {
                Debug.Log($"[ConquestSessionManager] Online Services: {ex.Message}. LAN / Direct Connect available.");
                m_ServicesInitialized = false;
                m_StatusMessage = "Local / LAN Mode (Direct Connect)";
                OnStatusChanged?.Invoke(m_StatusMessage);
                return false;
            }
        }

        public void StartSoloGame(string mapSceneName, int tickets = 150, int botsPerTeam = 4, BotDifficulty difficulty = BotDifficulty.Normal)
        {
            isSoloMode = true;
            sessionName = "Solo Practice";
            reinforcementTickets = tickets;
            targetTeamSize = botsPerTeam;
            enableBotFilling = true;
            botDifficulty = difficulty;

            EnsureNetworkManagerConfigured("127.0.0.1", 7777);

            if (NetworkManager.Singleton != null)
            {
                if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
                {
                    NetworkManager.Singleton.Shutdown();
                }

                NetworkManager.Singleton.StartHost();
                ConquestSceneTransitionManager.Instance?.TransitionToMap(mapSceneName, true);
            }
        }

        public bool StartHostGame(string mapSceneName, string sessName, int maxPlayers, int tickets, int teamSize, bool botFill, BotDifficulty diff)
        {
            isSoloMode = false;
            sessionName = sessName;
            maxHumanPlayers = maxPlayers;
            reinforcementTickets = tickets;
            targetTeamSize = teamSize;
            enableBotFilling = botFill;
            botDifficulty = diff;
            lastJoinCode = $"{UnityEngine.Random.Range(1000, 9999)}-CQ";

            EnsureNetworkManagerConfigured(connectAddress, connectPort);

            if (NetworkManager.Singleton != null)
            {
                if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
                {
                    NetworkManager.Singleton.Shutdown();
                }

                bool success = NetworkManager.Singleton.StartHost();
                if (success)
                {
                    m_StatusMessage = $"Hosting session on {connectAddress}:{connectPort}";
                    OnStatusChanged?.Invoke(m_StatusMessage);
                    return true;
                }
            }

            m_StatusMessage = "Failed to start Host session";
            OnStatusChanged?.Invoke(m_StatusMessage);
            return false;
        }

        public bool StartClientConnect(string address, ushort port)
        {
            isSoloMode = false;
            connectAddress = string.IsNullOrEmpty(address) ? "127.0.0.1" : address;
            connectPort = port > 0 ? port : (ushort)7777;

            EnsureNetworkManagerConfigured(connectAddress, connectPort);

            if (NetworkManager.Singleton != null)
            {
                if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
                {
                    NetworkManager.Singleton.Shutdown();
                }

                bool success = NetworkManager.Singleton.StartClient();
                if (success)
                {
                    m_StatusMessage = $"Connecting to {connectAddress}:{connectPort}...";
                    OnStatusChanged?.Invoke(m_StatusMessage);
                    return true;
                }
            }

            m_StatusMessage = "Failed to start client connection";
            OnStatusChanged?.Invoke(m_StatusMessage);
            return false;
        }

        public void LeaveSession()
        {
            m_StatusMessage = "Leaving session...";
            OnStatusChanged?.Invoke(m_StatusMessage);

            if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
            {
                NetworkManager.Singleton.Shutdown();
            }

            ConquestSceneTransitionManager.Instance?.ReturnToMainMenu();
        }

        public void EnsureNetworkManagerConfigured(string address, ushort port)
        {
            if (NetworkManager.Singleton != null)
            {
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                if (transport != null)
                {
                    transport.ConnectionData.Address = address;
                    transport.ConnectionData.Port = port;
                }
            }
        }
    }
}
