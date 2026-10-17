using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Classes;
using Game.Conquest.Gameplay;
using Game.Conquest.Maps;
using Game.Conquest.Networking;
using Game.Conquest.Scenes;

namespace Game.Conquest.UI
{
    /// <summary>
    /// Drives the MatchResultsCanvas prefab in the MatchResults scene. Every peer shows the results
    /// the server built when the match ended; the server also hands them to clients as they arrive
    /// and, after the countdown (or the host's button), takes everyone back to the lobby still connected.
    /// </summary>
    public class ConquestMatchResultsScreen : MonoBehaviour
    {
        [Header("Panel UI References")]
        [SerializeField] private MatchSummaryPanelUI summaryPanelUI;
        [SerializeField] private TeamResultsPanelUI redTeamPanelUI;
        [SerializeField] private TeamResultsPanelUI blueTeamPanelUI;

        [Header("Data")]
        [SerializeField] private ConquestMapRegistry mapRegistry;

        [Header("Colors")]
        [SerializeField] private Color victoryColor = new Color(0.3f, 0.85f, 0.4f);
        [SerializeField] private Color defeatColor = new Color(0.9f, 0.3f, 0.3f);
        [SerializeField] private Color neutralTitleColor = Color.white;
        [SerializeField] private Color redColor = new Color(0.75f, 0.2f, 0.2f, 0.95f);
        [SerializeField] private Color blueColor = new Color(0.2f, 0.45f, 0.85f, 0.95f);
        [SerializeField] private Color rowColor = new Color(0.12f, 0.16f, 0.24f, 0.9f);
        [SerializeField] private Color botRowColor = new Color(0.1f, 0.12f, 0.16f, 0.75f);

        private MatchResults m_Shown;
        private float m_ReturnAt = -1f;
        private bool m_Returning;
        private NetworkSceneManager m_SubscribedSceneManager;
        private readonly List<MatchResultRow> m_Sorted = new List<MatchResultRow>();

        // A dedicated server still runs this screen's server side (sending results, returning
        // everyone to the lobby) but skips everything that is only for a player to look at.
        private bool m_Headless;

        private void Start()
        {
            m_Headless = ConquestServerLaunchArgs.IsDedicatedServerLaunch();
            if (!m_Headless)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (summaryPanelUI != null && summaryPanelUI.btn_ReturnNow != null)
            {
                summaryPanelUI.btn_ReturnNow.onClick.AddListener(ReturnToLobby);
            }

            var nm = NetworkManager.Singleton;
            bool isServer = nm != null && nm.IsServer;
            summaryPanelUI?.SetReturnNowVisible(nm != null && nm.IsHost);

            var session = ConquestSessionManager.Instance;
            if (session != null)
            {
                session.OnMatchResultsAvailable += Show;
                if (session.LastMatchResults != null) Show(session.LastMatchResults);
            }
            if (m_Shown == null)
            {
                summaryPanelUI?.SetOutcome("MATCH OVER", neutralTitleColor, "Waiting for results from the server...");
            }

            if (isServer)
            {
                m_ReturnAt = Time.unscaledTime + (session != null ? session.resultsScreenSeconds : 15f);

                // Clients that are already here get the results now; the rest when they finish loading.
                foreach (var clientId in nm.ConnectedClientsIds)
                {
                    session?.SendMatchResultsTo(clientId);
                }
                if (nm.SceneManager != null)
                {
                    m_SubscribedSceneManager = nm.SceneManager;
                    m_SubscribedSceneManager.OnSceneEvent += HandleSceneEvent;
                }
            }
        }

        private void OnDestroy()
        {
            if (ConquestSessionManager.Instance != null)
            {
                ConquestSessionManager.Instance.OnMatchResultsAvailable -= Show;
            }
            if (m_SubscribedSceneManager != null)
            {
                m_SubscribedSceneManager.OnSceneEvent -= HandleSceneEvent;
            }
        }

        private void HandleSceneEvent(SceneEvent sceneEvent)
        {
            if (sceneEvent.SceneEventType == SceneEventType.LoadComplete ||
                sceneEvent.SceneEventType == SceneEventType.SynchronizeComplete)
            {
                ConquestSessionManager.Instance?.SendMatchResultsTo(sceneEvent.ClientId);
            }
        }

        private void Update()
        {
            var nm = NetworkManager.Singleton;
            bool isServer = nm != null && nm.IsServer;

            if (!isServer && m_Shown != null && m_ReturnAt < 0f)
            {
                // Clients count down from when the results arrived; the server decides the actual move.
                m_ReturnAt = Time.unscaledTime + m_Shown.returnToLobbySeconds;
            }

            if (m_ReturnAt >= 0f && !m_Headless)
            {
                int remaining = Mathf.CeilToInt(Mathf.Max(0f, m_ReturnAt - Time.unscaledTime));
                summaryPanelUI?.SetCountdown(m_Returning ? "Returning to the lobby..." : $"Returning to the lobby in {remaining}s");
            }

            if (isServer && !m_Returning && m_ReturnAt >= 0f && Time.unscaledTime >= m_ReturnAt)
            {
                ReturnToLobby();
            }
        }

        private void ReturnToLobby()
        {
            var nm = NetworkManager.Singleton;
            if (m_Returning || nm == null || !nm.IsServer) return;
            m_Returning = true;

            string lobbyScene = ConquestSessionManager.Instance != null ? ConquestSessionManager.Instance.lobbySceneName : "MainMenu";
            ConquestSceneTransitionManager.Instance?.TransitionToMap(lobbyScene, true);
        }

        private void Show(MatchResults results)
        {
            if (results == null) return;
            m_Shown = results;

            Team localTeam = FindLocalTeam(results);
            string title;
            Color titleColor;
            if (localTeam == Team.Neutral)
            {
                title = $"{results.winner.ToString().ToUpper()} TEAM WINS";
                titleColor = results.winner == Team.Red ? redColor : blueColor;
            }
            else
            {
                bool won = localTeam == results.winner;
                title = won ? "VICTORY" : "DEFEAT";
                titleColor = won ? victoryColor : defeatColor;
            }

            summaryPanelUI?.SetOutcome(title, titleColor, $"{results.winner} team won the battle");
            summaryPanelUI?.SetMatchInfo($"{MapLabel(results.mapScene.ToString())}  |  {FormatDuration(results.durationSeconds)}  |  " +
                                         $"Tickets left: Red {results.redTickets} / Blue {results.blueTickets}");

            FillTeam(redTeamPanelUI, results, Team.Red, "RED TEAM", redColor);
            FillTeam(blueTeamPanelUI, results, Team.Blue, "BLUE TEAM", blueColor);
        }

        private void FillTeam(TeamResultsPanelUI panel, MatchResults results, Team team, string teamName, Color teamColor)
        {
            if (panel == null) return;

            m_Sorted.Clear();
            int kills = 0, deaths = 0;
            foreach (var row in results.rows)
            {
                if (row.team != team) continue;
                m_Sorted.Add(row);
                kills += row.kills;
                deaths += row.deaths;
            }
            // Most kills first; fewer deaths breaks ties.
            m_Sorted.Sort((a, b) => a.kills != b.kills ? b.kills.CompareTo(a.kills) : a.deaths.CompareTo(b.deaths));

            panel.SetHeader(teamName, $"Kills {kills}  Deaths {deaths}", teamColor);
            panel.ClearRows();
            foreach (var row in m_Sorted)
            {
                string name = row.isBot ? $"{row.playerName} [BOT]" : row.playerName.ToString();
                panel.AddRow(name, ClassLabel(row.classIndex), row.kills, row.deaths, row.isBot ? botRowColor : rowColor);
            }
        }

        // Neutral for a dedicated server (no local player), which then just names the winner.
        private static Team FindLocalTeam(MatchResults results)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsClient) return Team.Neutral;
            foreach (var row in results.rows)
            {
                if (!row.isBot && row.clientId == nm.LocalClientId) return row.team;
            }
            return Team.Neutral;
        }

        private static string ClassLabel(int classIndex)
        {
            return System.Enum.IsDefined(typeof(InfantryClassType), classIndex)
                ? ((InfantryClassType)classIndex).ToString()
                : $"Class {classIndex}";
        }

        private string MapLabel(string sceneName)
        {
            var map = mapRegistry != null ? mapRegistry.GetMapBySceneName(sceneName) : null;
            return map != null && map.sceneName == sceneName ? map.displayName : sceneName;
        }

        private static string FormatDuration(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return $"{total / 60}:{total % 60:00}";
        }
    }
}
