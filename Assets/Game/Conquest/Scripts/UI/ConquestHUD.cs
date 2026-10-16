using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Gameplay;
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

        [Header("Team Colors")]
        [SerializeField] private Color redColor = new Color(0.85f, 0.2f, 0.2f, 0.9f);
        [SerializeField] private Color blueColor = new Color(0.2f, 0.5f, 0.9f, 0.9f);
        [SerializeField] private Color neutralColor = new Color(0.4f, 0.4f, 0.45f, 0.9f);
        [SerializeField] private Color contestedColor = new Color(0.9f, 0.75f, 0.1f, 0.95f);

        [Header("Result Colors")]
        [SerializeField] private Color victoryColor = Color.green;
        [SerializeField] private Color defeatColor = Color.red;

        [Header("Hitmarker")]
        [SerializeField] private float hitmarkerDuration = 0.15f;

        private float m_HitmarkerEndTime;

        private void Awake()
        {
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
            }
            HideTransientPanels();
        }

        private void OnDestroy()
        {
            if (respawnPanelUI != null)
            {
                respawnPanelUI.ClassSelected -= HandleClassSelected;
                respawnPanelUI.SpawnPostSelected -= HandleSpawnPostSelected;
            }
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

        // Panels that only appear for a reason (in a capture zone, dead, match over) start hidden.
        private void HideTransientPanels()
        {
            if (captureBarUI != null) captureBarUI.Hide();
            if (respawnPanelUI != null) respawnPanelUI.Hide();
            if (matchResultPanelUI != null) matchResultPanelUI.Hide();
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
                    if (post != null && Vector3.Distance(post.transform.position, playerPos) <= post.CaptureRadius)
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
            respawnPanelUI.SetTimer($"Respawning in {player.RemainingRespawnTime:0.0} seconds...");
            respawnPanelUI.SetSelectedClass(player.CurrentClassIndex);
            if (match != null)
            {
                respawnPanelUI.SetSpawnPosts(match.GetFriendlyPostNames(player.CurrentTeam), player.SelectedPostName);
            }
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
