using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Game.Conquest.Gameplay;
using Game.Conquest.Networking;
using Game.Conquest.Objectives;
using Game.Conquest.Player;

namespace Game.Conquest.UI
{
    /// <summary>
    /// Drives the HUDCanvas prefab: reads match and local-player state each frame and pushes it into
    /// the panel components. Layout and visuals live in the prefab, not in code.
    /// </summary>
    public class ConquestHUD : MonoBehaviour
    {
        public static ConquestHUD Instance { get; private set; }

        [Header("Canvas")]
        [SerializeField] private Canvas hudCanvas;

        [Header("Panel UI References")]
        [SerializeField] private TicketBarUI ticketBarUI;
        [SerializeField] private CommandPostsRowUI commandPostsRowUI;
        [SerializeField] private CaptureBarUI captureBarUI;
        [SerializeField] private CrosshairUI crosshairUI;
        [SerializeField] private PlayerStatsUI playerStatsUI;
        [SerializeField] private RespawnPanelUI respawnPanelUI;
        [SerializeField] private MatchResultPanelUI matchResultPanelUI;
        [SerializeField] private AllPostsAlertUI allPostsAlertUI;
        [SerializeField] private KillFeedUI killFeedUI;
        [SerializeField] private ScoreboardUI scoreboardUI;

        [Header("Team Colors")]
        [SerializeField] private Color redColor = new Color(0.85f, 0.2f, 0.2f, 0.9f);
        [SerializeField] private Color blueColor = new Color(0.2f, 0.5f, 0.9f, 0.9f);
        [SerializeField] private Color neutralColor = new Color(0.4f, 0.4f, 0.45f, 0.9f);
        [SerializeField] private Color contestedColor = new Color(0.9f, 0.75f, 0.1f, 0.95f);

        [Header("Result Colors")]
        [SerializeField] private Color victoryColor = Color.green;
        [SerializeField] private Color defeatColor = Color.red;

        [Header("Scoreboard Rows")]
        [SerializeField] private Color rowColor = new Color(0.12f, 0.16f, 0.24f, 0.9f);
        [SerializeField] private Color botRowColor = new Color(0.1f, 0.12f, 0.16f, 0.75f);

        [Header("Hitmarker")]
        [SerializeField] private float hitmarkerDuration = 0.15f;

        private float m_HitmarkerEndTime;
        private ConquestMatchManager m_StatsSource;
        private bool m_ScoreboardDirty = true;
        private readonly List<MatchResultRow> m_StatsCopy = new List<MatchResultRow>();

        private void Awake()
        {
            // A dedicated server has no local player, so there is nothing to show.
            if (ConquestServerLaunchArgs.IsDedicatedServerLaunch())
            {
                if (hudCanvas != null) hudCanvas.enabled = false;
                enabled = false;
                return;
            }

            // The map scenes used to carry a code-built HUD. Its old component has no canvas
            // assigned; it steps aside so the HUDCanvas prefab can drive the HUD instead.
            if (hudCanvas == null)
            {
                Debug.LogWarning($"[ConquestHUD] '{name}' has no HUD canvas assigned. Add the HUDCanvas prefab to this scene and remove this component.", this);
                enabled = false;
                return;
            }

            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[ConquestHUD] More than one HUD in the scene; disabling the extra one.", this);
                gameObject.SetActive(false);
                return;
            }
            Instance = this;

            if (respawnPanelUI != null)
            {
                respawnPanelUI.ClassSelected += HandleClassSelected;
                respawnPanelUI.SpawnPostSelected += HandleSpawnPostSelected;
                respawnPanelUI.DeployClicked += HandleDeployClicked;
            }
            ConquestMatchManager.KillReported += HandleKillReported;
            HideTransientPanels();
        }

        private void OnDestroy()
        {
            if (respawnPanelUI != null)
            {
                respawnPanelUI.ClassSelected -= HandleClassSelected;
                respawnPanelUI.SpawnPostSelected -= HandleSpawnPostSelected;
                respawnPanelUI.DeployClicked -= HandleDeployClicked;
            }
            ConquestMatchManager.KillReported -= HandleKillReported;
            WatchStats(null);
            if (Instance == this) Instance = null;
        }

        public static void TriggerHitmarker()
        {
            if (Instance != null)
            {
                Instance.m_HitmarkerEndTime = Time.time + Instance.hitmarkerDuration;
            }
        }

        // The server applies a class picked while dead to this respawn; while alive it is queued.
        private void HandleClassSelected(int classIndex)
        {
            var player = ConquestPlayer.Local;
            if (player != null) player.SelectClass(classIndex);
        }

        private void HandleSpawnPostSelected(string postName)
        {
            var player = ConquestPlayer.Local;
            if (player != null) player.SelectSpawnPost(postName);
        }

        private void HandleDeployClicked()
        {
            var player = ConquestPlayer.Local;
            var match = ConquestMatchManager.Instance;
            if (player == null || match == null || !CanDeploy(match, player, match.GetFriendlyPostNames(player.CurrentTeam))) return;
            player.Deploy(player.PendingClassIndex, player.SelectedPostName);
        }

        // A post must be picked whenever the team has one; with none left the player deploys at
        // the team spawn, so losing every post never locks anyone out.
        private static bool CanDeploy(ConquestMatchManager match, ConquestPlayer player, List<string> posts)
        {
            if (match.IsMatchOver || player.RemainingRespawnTime > 0f) return false;
            if (posts.Count == 0) return true;
            for (int i = 0; i < posts.Count; i++)
            {
                if (posts[i] == player.SelectedPostName) return true;
            }
            return false;
        }

        // Panels that only appear for a reason (in a capture zone, dead, match over) start hidden.
        private void HideTransientPanels()
        {
            if (captureBarUI != null) captureBarUI.Hide();
            if (respawnPanelUI != null) respawnPanelUI.Hide();
            if (matchResultPanelUI != null) matchResultPanelUI.Hide();
            if (allPostsAlertUI != null) allPostsAlertUI.Hide();
            if (scoreboardUI != null) scoreboardUI.Hide();
        }

        private void Update()
        {
            var nm = NetworkManager.Singleton;
            bool inSession = nm != null && (nm.IsClient || nm.IsServer);
            hudCanvas.enabled = inSession;
            if (!inSession) return;

            var match = ConquestMatchManager.Instance;
            var player = ConquestPlayer.Local;

            // Each panel updates on its own and tolerates missing state, so one missing
            // reference can't stop the rest of the HUD from filling in.
            UpdateTicketBar(match);
            UpdateCommandPosts(match);
            UpdateCaptureBar(match, player);
            UpdateCrosshair(player);
            UpdatePlayerStats(player);
            UpdateRespawnPanel(match, player);
            UpdateMatchResult(match, player);
            UpdateAllPostsAlert(match, player);
            UpdateScoreboard(match);
        }

        // "Killer [weapon] Victim", each name in its team colour; falls just name the victim.
        private void HandleKillReported(KillReport report)
        {
            if (killFeedUI == null) return;
            string victim = $"<color=#{ColorUtility.ToHtmlStringRGB(TeamColor(report.victimTeam))}>{report.victimName}</color>";
            if (!report.hasKiller)
            {
                killFeedUI.Push($"{victim} died");
                return;
            }
            string killer = $"<color=#{ColorUtility.ToHtmlStringRGB(TeamColor(report.killerTeam))}>{report.killerName}</color>";
            killFeedUI.Push($"{killer} [{report.weaponName}] {victim}");
        }

        // Held Tab shows the scoreboard. Rows are rebuilt only when it opens or the stats change.
        private void UpdateScoreboard(ConquestMatchManager match)
        {
            if (scoreboardUI == null) return;

            bool wanted = match != null && !match.IsMatchOver && Keyboard.current != null && Keyboard.current.tabKey.isPressed;
            if (!wanted)
            {
                scoreboardUI.Hide();
                return;
            }

            WatchStats(match);
            if (scoreboardUI.IsShown && !m_ScoreboardDirty) return;

            scoreboardUI.Show();
            m_ScoreboardDirty = false;
            // NetworkList isn't enumerable, so take a plain copy for the team columns.
            m_StatsCopy.Clear();
            for (int i = 0; i < match.liveStats.Count; i++) m_StatsCopy.Add(match.liveStats[i]);
            if (scoreboardUI.redTeamPanel != null)
                scoreboardUI.redTeamPanel.ShowTeam(m_StatsCopy, Team.Red, "RED TEAM", redColor, rowColor, botRowColor);
            if (scoreboardUI.blueTeamPanel != null)
                scoreboardUI.blueTeamPanel.ShowTeam(m_StatsCopy, Team.Blue, "BLUE TEAM", blueColor, rowColor, botRowColor);
        }

        private void WatchStats(ConquestMatchManager match)
        {
            if (m_StatsSource == match) return;
            if (m_StatsSource != null) m_StatsSource.liveStats.OnListChanged -= HandleStatsChanged;
            m_StatsSource = match;
            if (m_StatsSource != null) m_StatsSource.liveStats.OnListChanged += HandleStatsChanged;
            m_ScoreboardDirty = true;
        }

        private void HandleStatsChanged(NetworkListEvent<MatchResultRow> change)
        {
            m_ScoreboardDirty = true;
        }

        private Color TeamColor(Team team)
        {
            if (team == Team.Red) return redColor;
            if (team == Team.Blue) return blueColor;
            return neutralColor;
        }

        private void UpdateTicketBar(ConquestMatchManager match)
        {
            if (ticketBarUI == null || match == null) return;
            ticketBarUI.SetTickets(match.RedTickets, match.BlueTickets);
        }

        private void UpdateCommandPosts(ConquestMatchManager match)
        {
            if (commandPostsRowUI == null || match == null || match.CommandPosts == null) return;

            var posts = match.CommandPosts;
            int shown = 0;
            for (int i = 0; i < posts.Count; i++)
            {
                var post = posts[i];
                if (post == null) continue;

                var icon = commandPostsRowUI.GetIcon(shown);
                if (icon == null) return;

                string label = post.PostName;
                if (post.IsContested) label += " (!)";
                else if (post.CaptureProgress > 0f && post.CaptureProgress < 1f) label += $" {(int)(post.CaptureProgress * 100)}%";

                icon.SetPost(label, post.IsContested ? contestedColor : TeamColor(post.CurrentOwner));
                shown++;
            }
            commandPostsRowUI.SetVisibleCount(shown);
        }

        private void UpdateCaptureBar(ConquestMatchManager match, ConquestPlayer player)
        {
            if (captureBarUI == null) return;

            ConquestCommandPost nearbyPost = null;
            if (match != null && player != null && !player.Health.IsDead)
            {
                Vector3 playerPos = player.transform.position;
                foreach (var post in match.CommandPosts)
                {
                    if (post != null && post.IsInCaptureZone(playerPos))
                    {
                        nearbyPost = post;
                        break;
                    }
                }
            }

            if (nearbyPost == null)
            {
                captureBarUI.Hide();
                return;
            }

            captureBarUI.Show();
            float progress = Mathf.Clamp01(nearbyPost.CaptureProgress);
            Color color = nearbyPost.IsContested ? contestedColor : TeamColor(nearbyPost.capturingTeam.Value);
            string status = nearbyPost.IsContested
                ? $"[CONTESTED] {nearbyPost.PostName}"
                : $"Capturing {nearbyPost.PostName} ({(int)(progress * 100)}%)";
            captureBarUI.SetProgress(progress, color, status);
        }

        private void UpdateCrosshair(ConquestPlayer player)
        {
            if (crosshairUI == null) return;

            if (player == null || player.Health.IsDead)
            {
                crosshairUI.Hide();
                return;
            }

            crosshairUI.Show();
            crosshairUI.SetHitmarker(Time.time < m_HitmarkerEndTime);
        }

        private void UpdatePlayerStats(ConquestPlayer player)
        {
            if (playerStatsUI == null) return;

            if (player == null || player.Health.IsDead)
            {
                playerStatsUI.Hide();
                return;
            }

            playerStatsUI.Show();
            playerStatsUI.SetTeamColor(TeamColor(player.CurrentTeam));

            string className = "Standard";
            var classes = player.AvailableClasses;
            if (classes != null && player.CurrentClassIndex >= 0 && player.CurrentClassIndex < classes.Count && classes[player.CurrentClassIndex] != null)
            {
                className = classes[player.CurrentClassIndex].className;
            }

            string ammo = "";
            var weapon = player.Weapon;
            if (weapon != null)
            {
                ammo = weapon.IsReloading ? "AMMO: RELOADING..." : $"AMMO: {weapon.CurrentAmmo} / {weapon.MagazineCapacity} [R]";
                if (weapon.HasSupportStation) ammo += "  |  [Q] Station";
                if (weapon.HasZoom) ammo += "  |  [RMB] Scope";
            }

            playerStatsUI.SetStats(
                $"{player.CurrentTeam}  |  {className}",
                $"HEALTH: {player.Health.CurrentHealth} / {player.Health.MaxHealth}",
                ammo);
        }

        private void UpdateRespawnPanel(ConquestMatchManager match, ConquestPlayer player)
        {
            if (respawnPanelUI == null) return;

            if (player == null || !player.Health.IsDead)
            {
                respawnPanelUI.Hide();
                return;
            }

            respawnPanelUI.Show();
            respawnPanelUI.SetSelectedClass(player.CurrentClassIndex);
            if (match == null) return;

            var posts = match.GetFriendlyPostNames(player.CurrentTeam);
            respawnPanelUI.SetSpawnPosts(posts, player.SelectedPostName);
            bool canDeploy = CanDeploy(match, player, posts);
            respawnPanelUI.SetDeployInteractable(canDeploy);

            float wait = player.RemainingRespawnTime;
            if (wait > 0f) respawnPanelUI.SetTimer($"Deploy in {wait:0.0} seconds");
            else if (canDeploy) respawnPanelUI.SetTimer(posts.Count == 0 ? "No safe command post: deploy at base" : "Ready to deploy");
            else respawnPanelUI.SetTimer("Choose a command post to deploy");
        }

        private void UpdateAllPostsAlert(ConquestMatchManager match, ConquestPlayer player)
        {
            if (allPostsAlertUI == null) return;

            if (match == null || match.IsMatchOver || match.AllPostsHolder == Team.Neutral)
            {
                allPostsAlertUI.Hide();
                return;
            }

            Team holder = match.AllPostsHolder;
            allPostsAlertUI.Show();
            allPostsAlertUI.SetCountdown(
                Mathf.CeilToInt(match.AllPostsSecondsLeft),
                player != null && player.CurrentTeam == holder,
                TeamColor(holder));
        }

        private void UpdateMatchResult(ConquestMatchManager match, ConquestPlayer player)
        {
            if (matchResultPanelUI == null) return;

            if (match == null || !match.IsMatchOver)
            {
                matchResultPanelUI.Hide();
                return;
            }

            matchResultPanelUI.Show();
            bool isWinner = player != null && player.CurrentTeam == match.WinningTeam;
            matchResultPanelUI.SetResult(
                isWinner ? "VICTORY!" : "DEFEAT",
                isWinner ? victoryColor : defeatColor,
                $"{match.WinningTeam} TEAM WON THE BATTLE!");
        }
    }
}
