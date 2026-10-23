using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Combat;
using Game.Conquest.Controls;
using Game.Conquest.Gameplay;
using Game.Conquest.Networking;
using Game.Conquest.Objectives;
using Game.Conquest.Player;
using Game.Conquest.Settings;

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
        [SerializeField] private DamageIndicatorUI damageIndicatorUI;
        [SerializeField] private LowHealthUI lowHealthUI;
        [SerializeField] private WorldMarkersUI worldMarkersUI;
        [Tooltip("Small command map in the corner while alive.")]
        [SerializeField] private CommandMapUI commandMapUI;
        [SerializeField] private ScopeOverlayUI scopeOverlayUI;

        [Header("Theme")]
        [Tooltip("Team colours (and the colour-blind palette), contested colour and team icons.")]
        [SerializeField] private ConquestUITheme theme;

        [Header("Result Colors")]
        [SerializeField] private Color victoryColor = Color.green;
        [SerializeField] private Color defeatColor = Color.red;

        [Header("Scoreboard Rows")]
        [SerializeField] private Color rowColor = new Color(0.12f, 0.16f, 0.24f, 0.9f);
        [SerializeField] private Color botRowColor = new Color(0.1f, 0.12f, 0.16f, 0.75f);

        [Header("Hitmarker")]
        [SerializeField] private float hitmarkerDuration = 0.15f;

        [Header("Health")]
        [Tooltip("Placeholder: health fraction at or below which the health bar turns red.")]
        [Range(0f, 1f)]
        [SerializeField] private float lowHealthFraction = 0.35f;

        private float m_HitmarkerEndTime;
        private bool m_HitmarkerKill;
        // Current key labels for the HUD hints, refreshed when the player rebinds.
        private string m_ReloadKey = "R";
        private string m_DeployKey = "Q";
        private string m_SwitchKey = "E";
        private string m_AimKey = "RMB";
        private ConquestMatchManager m_StatsSource;
        private bool m_ScoreboardDirty = true;
        private readonly List<MatchResultRow> m_StatsCopy = new List<MatchResultRow>();
        // Delegates made once so the per-frame marker and map updates don't allocate closures.
        private System.Func<ConquestCommandPost, Color> m_PostColor;
        private System.Func<ConquestCommandPost, Color> m_CaptureColor;
        // Inputs of the last hints line, so it is only rebuilt when one of them changes.
        private int m_HintsKey = int.MinValue;
        // Change keys for the per-frame text, so strings are only built when what they show changes.
        private readonly List<int> m_PostLabelKeys = new List<int>();
        private readonly List<string> m_PostLabels = new List<string>();
        private readonly List<ConquestCommandPost> m_PostLabelPosts = new List<ConquestCommandPost>();
        private readonly List<string> m_FriendlyPosts = new List<string>();
        private ConquestCommandPost m_CapturePost;
        private int m_CaptureKey = int.MinValue;
        private string m_CaptureStatus = "";
        private int m_TimerKey = int.MinValue;
        private string m_HintsRefusal = "";

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
            m_PostColor = PostColor;
            m_CaptureColor = post => post.IsContested ? ContestedColor : TeamColor(post.capturingTeam.Value);
            ConquestMatchManager.KillReported += HandleKillReported;
            ConquestInput.BindingsChanged += RefreshKeyLabels;
            RefreshKeyLabels();
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
            ConquestInput.BindingsChanged -= RefreshKeyLabels;
            WatchStats(null);
            if (Instance == this) Instance = null;
        }

        /// <param name="killed">The shot also killed: the marker shows in the kill colour, for longer.</param>
        public static void TriggerHitmarker(bool killed = false)
        {
            var hud = Instance;
            if (hud == null) return;
            // A plain hit doesn't cut a kill marker short.
            if (hud.m_HitmarkerKill && !killed && Time.time < hud.m_HitmarkerEndTime) return;
            hud.m_HitmarkerKill = killed;
            hud.m_HitmarkerEndTime = Time.time + hud.hitmarkerDuration * (killed ? 3f : 1f);
        }

        /// <summary>Local player was hurt by a shot fired from <paramref name="sourcePosition"/>.</summary>
        public static void ShowDamageFrom(Vector3 sourcePosition)
        {
            if (Instance != null && Instance.damageIndicatorUI != null)
            {
                Instance.damageIndicatorUI.ShowFrom(sourcePosition);
            }
        }

        private void RefreshKeyLabels()
        {
            m_ReloadKey = ConquestInput.KeyLabel(ConquestInput.Gameplay.Reload);
            m_DeployKey = ConquestInput.KeyLabel(ConquestInput.Gameplay.Deploy);
            m_SwitchKey = ConquestInput.KeyLabel(ConquestInput.Gameplay.SwitchWeapon);
            m_AimKey = ConquestInput.KeyLabel(ConquestInput.Gameplay.Aim);
            m_HintsKey = int.MinValue;
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
            if (player == null || match == null) return;
            match.GetFriendlyPostNames(player.CurrentTeam, m_FriendlyPosts);
            if (!CanDeploy(match, player, m_FriendlyPosts)) return;
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
            if (worldMarkersUI != null) worldMarkersUI.HideAll();
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
            if (damageIndicatorUI != null)
            {
                damageIndicatorUI.Tick(player != null && !player.Health.IsDead ? player.transform : null);
            }
            UpdatePlayerStats(player);
            UpdateWorldMarkers(match, player);
            UpdateCommandMap(match, player);
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

            bool wanted = match != null && !match.IsMatchOver && ConquestInput.ScoreboardHeld;
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
                scoreboardUI.redTeamPanel.ShowTeam(m_StatsCopy, Team.Red, "RED TEAM", TeamColor(Team.Red), rowColor, botRowColor);
            if (scoreboardUI.blueTeamPanel != null)
                scoreboardUI.blueTeamPanel.ShowTeam(m_StatsCopy, Team.Blue, "BLUE TEAM", TeamColor(Team.Blue), rowColor, botRowColor);
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

        private Color PostColor(ConquestCommandPost post)
        {
            return post.IsContested ? ContestedColor : TeamColor(post.CurrentOwner);
        }

        private Color ContestedColor => theme != null ? theme.contested : new Color(0.9f, 0.75f, 0.1f, 0.95f);

        // Literals, so callers can compare by reference instead of formatting the enum each frame.
        private static string TeamName(Team team)
        {
            if (team == Team.Red) return "RED";
            if (team == Team.Blue) return "BLUE";
            return "";
        }

        private Color TeamColor(Team team)
        {
            if (theme != null) return theme.TeamColor(team, ConquestSettingsManager.ColorBlindTeams);
            return team == Team.Red ? Color.red : team == Team.Blue ? Color.blue : Color.gray;
        }

        private Sprite TeamIcon(Team team) => theme != null ? theme.TeamIcon(team) : null;

        private void UpdateTicketBar(ConquestMatchManager match)
        {
            if (ticketBarUI == null || match == null) return;
            ticketBarUI.SetTickets(match.RedTickets, match.BlueTickets);
            ticketBarUI.SetTeamColors(TeamColor(Team.Red), TeamColor(Team.Blue));
        }

        private void UpdateCommandPosts(ConquestMatchManager match)
        {
            if (commandPostsRowUI == null || match == null || match.CommandPosts == null) return;

            var posts = match.CommandPosts;
            int shown = 0;
            for (int i = 0; i < posts.Count; i++)
            {
                var post = posts[i];
                // The row lists posts to fight over; home bases are on the map and markers instead.
                if (post == null || !post.IsCapturable) continue;

                var icon = commandPostsRowUI.GetIcon(shown);
                if (icon == null) return;

                // -2 contested, -1 settled, 0..99 capture percent.
                int key = post.IsContested ? -2 : (post.CaptureProgress > 0f && post.CaptureProgress < 1f ? (int)(post.CaptureProgress * 100) : -1);
                while (m_PostLabelKeys.Count <= shown)
                {
                    m_PostLabelKeys.Add(int.MinValue);
                    m_PostLabels.Add("");
                    m_PostLabelPosts.Add(null);
                }
                if (m_PostLabelKeys[shown] != key || m_PostLabelPosts[shown] != post)
                {
                    m_PostLabelKeys[shown] = key;
                    m_PostLabelPosts[shown] = post;
                    m_PostLabels[shown] = key == -2 ? post.PostName + " (!)" : key >= 0 ? $"{post.PostName} {key}%" : post.PostName;
                }

                icon.SetPost(m_PostLabels[shown], PostColor(post), TeamIcon(post.CurrentOwner));
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
                var posts = match.CommandPosts;
                for (int i = 0; i < posts.Count; i++)
                {
                    if (posts[i] != null && posts[i].IsCapturable && posts[i].IsInCaptureZone(playerPos))
                    {
                        nearbyPost = posts[i];
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
            Color color = nearbyPost.IsContested ? ContestedColor : TeamColor(nearbyPost.capturingTeam.Value);
            int key = nearbyPost.IsContested ? -1 : (int)(progress * 100);
            if (nearbyPost != m_CapturePost || key != m_CaptureKey)
            {
                m_CapturePost = nearbyPost;
                m_CaptureKey = key;
                m_CaptureStatus = key < 0 ? $"[CONTESTED] {nearbyPost.PostName}" : $"Capturing {nearbyPost.PostName} ({key}%)";
            }
            captureBarUI.SetProgress(progress, color, m_CaptureStatus);
        }

        private void UpdateCrosshair(ConquestPlayer player)
        {
            if (crosshairUI == null) return;

            if (player == null || player.Health.IsDead)
            {
                if (scopeOverlayUI != null) scopeOverlayUI.SetVisible(false);
                crosshairUI.Hide();
                return;
            }

            crosshairUI.Show();
            bool showing = Time.time < m_HitmarkerEndTime;
            crosshairUI.SetHitmarker(showing, showing && m_HitmarkerKill);

            // Open the lines to the spread cone: its half-angle projected onto the screen at the
            // camera's current field of view, in canvas pixels.
            var weapon = player.Weapon;
            var cam = player.Movement != null ? player.Movement.PlayerCamera : null;
            float gap = 0f;
            if (weapon != null && cam != null)
            {
                float halfHeight = ((RectTransform)hudCanvas.transform).rect.height * 0.5f;
                gap = Mathf.Tan(weapon.EffectiveSpreadAngle * Mathf.Deg2Rad) / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * halfHeight;
            }
            crosshairUI.SetAim(weapon != null ? weapon.Aim01 : 0f);
            crosshairUI.SetSpread(gap);
            // Scoped weapons draw their own reticle; every other aim keeps the crosshair.
            bool scoped = weapon != null && weapon.IsScoped;
            crosshairUI.SetLinesHidden(scoped);
            if (scopeOverlayUI != null) scopeOverlayUI.SetVisible(scoped);
        }

        private void UpdateWorldMarkers(ConquestMatchManager match, ConquestPlayer player)
        {
            if (worldMarkersUI == null) return;
            bool alive = player != null && !player.Health.IsDead && match != null && !match.IsMatchOver;
            var cam = alive && player.Movement != null ? player.Movement.PlayerCamera : null;
            if (cam == null)
            {
                worldMarkersUI.HideAll();
                return;
            }
            worldMarkersUI.Tick(cam, player, match.CommandPosts, m_PostColor, m_CaptureColor, TeamColor(player.CurrentTeam));
        }

        private void UpdateCommandMap(ConquestMatchManager match, ConquestPlayer player)
        {
            if (commandMapUI == null) return;
            bool show = match != null && player != null && !player.Health.IsDead && !match.IsMatchOver;
            if (commandMapUI.gameObject.activeSelf != show) commandMapUI.gameObject.SetActive(show);
            if (show) commandMapUI.Tick(match.CommandPosts, player, m_PostColor, TeamColor(player.CurrentTeam));
        }

        private void UpdatePlayerStats(ConquestPlayer player)
        {
            float healthFraction = -1f;
            if (player != null && !player.Health.IsDead && player.Health.MaxHealth > 0)
            {
                healthFraction = (float)player.Health.CurrentHealth / player.Health.MaxHealth;
            }
            if (lowHealthUI != null)
            {
                lowHealthUI.ReduceMotion = ConquestSettingsManager.ReduceMotion;
                lowHealthUI.Tick(healthFraction);
            }

            if (playerStatsUI == null) return;

            if (player == null || player.Health.IsDead)
            {
                playerStatsUI.Hide();
                return;
            }

            playerStatsUI.Show();
            playerStatsUI.SetTeamColor(TeamColor(player.CurrentTeam));
            playerStatsUI.SetTeamIcon(TeamIcon(player.CurrentTeam));

            var classData = player.CurrentClassData;
            playerStatsUI.SetTeamClass(TeamName(player.CurrentTeam), classData != null ? classData.className : "");
            playerStatsUI.SetHealth(player.Health.CurrentHealth, player.Health.MaxHealth, healthFraction <= lowHealthFraction);

            var weapon = player.Weapon;
            if (weapon == null)
            {
                playerStatsUI.SetAmmo(0, 0, false);
                playerStatsUI.SetHints("");
                return;
            }
            // Red weapons show a heat gauge, blue ones a magazine. Both read the local prediction,
            // so they respond the moment the trigger is pulled.
            // The repair tool has no ammo or heat: it shows its name on a full bar.
            bool tool = weapon.IsRepairTool;
            bool heat = weapon.UsesHeat && !tool;
            playerStatsUI.SetWeaponModel(heat);
            playerStatsUI.SetWeapon(weapon.ActiveWeaponName, weapon.ActiveWeaponIcon);
            if (tool) playerStatsUI.SetAmmoLabel(RepairToolAmmoLabel);
            else if (heat) playerStatsUI.SetHeat(Mathf.CeilToInt(weapon.Heat01 * 100f), weapon.IsOverheated);
            else playerStatsUI.SetAmmo(weapon.CurrentAmmo, weapon.MagazineCapacity, weapon.IsReloading);
            UpdateHints(weapon, player.Throwables);
        }

        private const string RepairToolAmmoLabel = "REPAIR";

        // "[R] Reload | [E] Switch | [Q] Grenade x2 | [RMB] Scope". Rebuilt only when the
        // weapon in hand, a count or cooldown second, the refusal message, the weapon model or the
        // key labels change, not every frame.
        private void UpdateHints(ConquestWeapon weapon, ConquestThrowables throwables)
        {
            string refusal = weapon.HasSupportStation ? weapon.StationRefusal : "";
            int cooldown = weapon.HasSupportStation ? Mathf.CeilToInt(weapon.StationCooldownRemaining) : 0;
            bool tool = weapon.IsRepairTool;
            bool heat = weapon.UsesHeat;
            bool throws = throwables != null && throwables.HasThrowable;
            int grenades = throws ? Mathf.Min(throwables.Count, 255) : 0;
            int key = (weapon.HasSupportStation ? 1 : 0) | (weapon.HasZoom ? 2 : 0) | (weapon.HasScope ? 4 : 0) | (heat ? 8 : 0) |
                      ((int)weapon.ActiveSlot << 4) | (weapon.HasSidearm ? 32 : 0) | (throws ? 64 : 0) | (grenades << 7) | (cooldown << 15);
            if (key == m_HintsKey && refusal == m_HintsRefusal) return;
            m_HintsKey = key;
            m_HintsRefusal = refusal;

            // Heat weapons vent by themselves, so there is no reload hint for them; the tool has no ammo.
            string hints = heat || tool ? "" : $"[{m_ReloadKey}] Reload";
            if (weapon.HasSidearm) hints = Join(hints, $"[{m_SwitchKey}] Switch");
            if (throws) hints = Join(hints, $"[{m_DeployKey}] {throwables.ThrowableName} x{grenades}");
            if (weapon.HasSupportStation)
            {
                if (refusal.Length > 0) hints = Join(hints, refusal);
                else if (cooldown > 0) hints = Join(hints, $"Station {cooldown}s");
                else hints = Join(hints, $"[{m_DeployKey}] Station");
            }
            if (weapon.HasZoom) hints = Join(hints, weapon.HasScope ? $"[{m_AimKey}] Scope" : $"[{m_AimKey}] Aim");
            playerStatsUI.SetHints(hints);
        }

        private static string Join(string line, string part) => line.Length == 0 ? part : $"{line}  |  {part}";

        private void UpdateRespawnPanel(ConquestMatchManager match, ConquestPlayer player)
        {
            if (respawnPanelUI == null) return;

            if (player == null || !player.Health.IsDead)
            {
                respawnPanelUI.Hide();
                return;
            }

            respawnPanelUI.Show();
            respawnPanelUI.SetClasses(player.AvailableClasses, player.CurrentClassIndex);
            if (match == null) return;

            var posts = m_FriendlyPosts;
            match.GetFriendlyPostNames(player.CurrentTeam, posts);
            respawnPanelUI.SetSpawnPosts(posts, player.SelectedPostName);
            bool canDeploy = CanDeploy(match, player, posts);
            respawnPanelUI.SetDeployInteractable(canDeploy);
            if (respawnPanelUI.commandMap != null)
            {
                respawnPanelUI.commandMap.Tick(match.CommandPosts, player, m_PostColor, TeamColor(player.CurrentTeam), posts, player.SelectedPostName);
            }

            // Tenths of a second while waiting; negative codes for the three ready states.
            float wait = player.RemainingRespawnTime;
            int timerKey = wait > 0f ? Mathf.CeilToInt(wait * 10f) : (canDeploy ? (posts.Count == 0 ? -1 : -2) : -3);
            if (timerKey != m_TimerKey)
            {
                m_TimerKey = timerKey;
                if (timerKey > 0) respawnPanelUI.SetTimer($"Deploy in {timerKey / 10f:0.0} seconds");
                else if (timerKey == -1) respawnPanelUI.SetTimer("No safe command post: deploy at base");
                else if (timerKey == -2) respawnPanelUI.SetTimer("Ready to deploy");
                else respawnPanelUI.SetTimer("Choose a command post to deploy");
            }
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
