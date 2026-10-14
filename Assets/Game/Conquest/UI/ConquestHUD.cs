using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Game.Conquest.Gameplay;
using Game.Conquest.Objectives;
using Game.Conquest.Player;

namespace Game.Conquest.UI
{
    public class ConquestHUD : MonoBehaviour
    {
        public static ConquestHUD Instance { get; private set; }

        private ConquestPlayer m_LocalPlayer;
        private float m_HitmarkerEndTime;

        // Canvas & TMP References
        private Canvas m_HUDCanvas;
        private CanvasScaler m_CanvasScaler;
        private TMP_FontAsset m_Font;

        // Ticket UI
        private TextMeshProUGUI m_RedTicketsText;
        private TextMeshProUGUI m_BlueTicketsText;

        // Command Posts UI
        private Transform m_PostsContainer;
        private readonly List<GameObject> m_PostElements = new List<GameObject>();
        private readonly List<TextMeshProUGUI> m_PostTexts = new List<TextMeshProUGUI>();
        private readonly List<UnityEngine.UI.Image> m_PostImages = new List<UnityEngine.UI.Image>();

        // Local Capture Bar
        private GameObject m_CaptureBarRoot;
        private UnityEngine.UI.Image m_CaptureBarFill;
        private TextMeshProUGUI m_CaptureStatusText;

        // Crosshair & Hitmarker
        private TextMeshProUGUI m_CrosshairText;

        // Player Stats
        private TextMeshProUGUI m_PlayerStatsText;
        private UnityEngine.UI.Image m_PlayerStatsBg;

        // Respawn & Class Selection Overlay
        private GameObject m_RespawnRoot;
        private TextMeshProUGUI m_RespawnTimerText;
        private Transform m_ClassButtonsContainer;
        private Transform m_SpawnPostButtonsContainer;
        private readonly List<UnityEngine.UI.Button> m_ClassButtons = new List<UnityEngine.UI.Button>();
        private readonly List<UnityEngine.UI.Button> m_SpawnPostButtons = new List<UnityEngine.UI.Button>();

        // Match Results Overlay
        private GameObject m_ResultRoot;
        private TextMeshProUGUI m_ResultTitleText;
        private TextMeshProUGUI m_ResultSubText;

        private void Awake()
        {
            Instance = this;
            BuildCanvasHUD();
        }

        public static void TriggerHitmarker()
        {
            if (Instance != null)
            {
                Instance.m_HitmarkerEndTime = Time.time + 0.15f;
            }
        }

        private void Start()
        {
            FindLocalPlayer();
            Game.Conquest.Scenes.ConquestSceneTransitionManager.Instance?.HideLoading();
        }

        private void FindLocalPlayer()
        {
            if (m_LocalPlayer != null) return;
            foreach (var p in FindObjectsByType<ConquestPlayer>(FindObjectsInactive.Exclude))
            {
                if (p.IsOwner && !p.isBot)
                {
                    m_LocalPlayer = p;
                    break;
                }
            }
        }

        private void BuildCanvasHUD()
        {
            m_Font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (m_Font == null) m_Font = TMP_Settings.defaultFontAsset;

            GameObject canvasGO = new GameObject("HUDCanvas");
            canvasGO.transform.SetParent(transform, false);

            m_HUDCanvas = canvasGO.AddComponent<Canvas>();
            m_HUDCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_HUDCanvas.sortingOrder = 50;

            m_CanvasScaler = canvasGO.AddComponent<CanvasScaler>();
            m_CanvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            m_CanvasScaler.referenceResolution = new Vector2(1920, 1080);
            m_CanvasScaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            // 1. TICKET HEADER BAR
            GameObject topBar = new GameObject("TicketTopBar");
            topBar.transform.SetParent(canvasGO.transform, false);
            var topRect = topBar.AddComponent<RectTransform>();
            topRect.anchorMin = new Vector2(0.5f, 1f);
            topRect.anchorMax = new Vector2(0.5f, 1f);
            topRect.pivot = new Vector2(0.5f, 1f);
            topRect.anchoredPosition = new Vector2(0, -15);
            topRect.sizeDelta = new Vector2(500, 45);

            // Red Tickets
            GameObject redBox = new GameObject("RedTickets");
            redBox.transform.SetParent(topBar.transform, false);
            var redImg = redBox.AddComponent<UnityEngine.UI.Image>();
            redImg.color = new Color(0.8f, 0.15f, 0.15f, 0.85f);
            var redRect = redBox.GetComponent<RectTransform>();
            redRect.anchorMin = new Vector2(0, 0);
            redRect.anchorMax = new Vector2(0.38f, 1);
            redRect.sizeDelta = Vector2.zero;
            m_RedTicketsText = CreateTMP(redBox, "RED: 250", 18, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);

            // Center Label
            GameObject centerBox = new GameObject("CenterTickets");
            centerBox.transform.SetParent(topBar.transform, false);
            var centerImg = centerBox.AddComponent<UnityEngine.UI.Image>();
            centerImg.color = new Color(0.1f, 0.12f, 0.16f, 0.85f);
            var centerRect = centerBox.GetComponent<RectTransform>();
            centerRect.anchorMin = new Vector2(0.40f, 0);
            centerRect.anchorMax = new Vector2(0.60f, 1);
            centerRect.sizeDelta = Vector2.zero;
            CreateTMP(centerBox, "TICKETS", 15, Color.yellow, TextAlignmentOptions.Center, FontStyles.Bold);

            // Blue Tickets
            GameObject blueBox = new GameObject("BlueTickets");
            blueBox.transform.SetParent(topBar.transform, false);
            var blueImg = blueBox.AddComponent<UnityEngine.UI.Image>();
            blueImg.color = new Color(0.15f, 0.40f, 0.90f, 0.85f);
            var blueRect = blueBox.GetComponent<RectTransform>();
            blueRect.anchorMin = new Vector2(0.62f, 0);
            blueRect.anchorMax = new Vector2(1, 1);
            blueRect.sizeDelta = Vector2.zero;
            m_BlueTicketsText = CreateTMP(blueBox, "BLUE: 250", 18, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);

            // 2. COMMAND POSTS ROW
            GameObject postsBar = new GameObject("CommandPostsRow");
            postsBar.transform.SetParent(canvasGO.transform, false);
            var postsRect = postsBar.AddComponent<RectTransform>();
            postsRect.anchorMin = new Vector2(0.5f, 1f);
            postsRect.anchorMax = new Vector2(0.5f, 1f);
            postsRect.pivot = new Vector2(0.5f, 1f);
            postsRect.anchoredPosition = new Vector2(0, -68);
            postsRect.sizeDelta = new Vector2(800, 36);

            var hLayout = postsBar.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 10;
            hLayout.childAlignment = TextAnchor.MiddleCenter;
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = true;
            m_PostsContainer = postsBar.transform;

            // 3. CAPTURE PROGRESS BAR (Lower Center)
            m_CaptureBarRoot = new GameObject("CaptureBarRoot");
            m_CaptureBarRoot.transform.SetParent(canvasGO.transform, false);
            var capRect = m_CaptureBarRoot.AddComponent<RectTransform>();
            capRect.anchorMin = new Vector2(0.5f, 0f);
            capRect.anchorMax = new Vector2(0.5f, 0f);
            capRect.pivot = new Vector2(0.5f, 0f);
            capRect.anchoredPosition = new Vector2(0, 110);
            capRect.sizeDelta = new Vector2(360, 32);

            var capBg = m_CaptureBarRoot.AddComponent<UnityEngine.UI.Image>();
            capBg.color = new Color(0.1f, 0.12f, 0.16f, 0.85f);

            GameObject fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(m_CaptureBarRoot.transform, false);
            m_CaptureBarFill = fillGO.AddComponent<UnityEngine.UI.Image>();
            m_CaptureBarFill.color = Color.white;
            var fillRect = fillGO.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0, 0);
            fillRect.anchorMax = new Vector2(1, 1);
            fillRect.pivot = new Vector2(0, 0.5f);
            fillRect.sizeDelta = Vector2.zero;

            GameObject statusTextGO = new GameObject("StatusText");
            statusTextGO.transform.SetParent(m_CaptureBarRoot.transform, false);
            m_CaptureStatusText = CreateTMP(statusTextGO, "Capturing Post", 16, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);

            m_CaptureBarRoot.SetActive(false);

            // 4. CROSSHAIR & HITMARKER
            GameObject crosshairGO = new GameObject("Crosshair");
            crosshairGO.transform.SetParent(canvasGO.transform, false);
            var crossRect = crosshairGO.AddComponent<RectTransform>();
            crossRect.anchorMin = new Vector2(0.5f, 0.5f);
            crossRect.anchorMax = new Vector2(0.5f, 0.5f);
            crossRect.pivot = new Vector2(0.5f, 0.5f);
            crossRect.sizeDelta = new Vector2(40, 40);
            m_CrosshairText = CreateTMP(crosshairGO, "+", 24, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.Center, FontStyles.Bold);

            // 5. PLAYER STATS CARD (Bottom-Left)
            GameObject statsGO = new GameObject("PlayerStatsCard");
            statsGO.transform.SetParent(canvasGO.transform, false);
            var statsRect = statsGO.AddComponent<RectTransform>();
            statsRect.anchorMin = new Vector2(0f, 0f);
            statsRect.anchorMax = new Vector2(0f, 0f);
            statsRect.pivot = new Vector2(0f, 0f);
            statsRect.anchoredPosition = new Vector2(25, 25);
            statsRect.sizeDelta = new Vector2(280, 110);

            m_PlayerStatsBg = statsGO.AddComponent<UnityEngine.UI.Image>();
            m_PlayerStatsBg.color = new Color(0.12f, 0.16f, 0.22f, 0.90f);

            GameObject statsTextGO = new GameObject("StatsText");
            statsTextGO.transform.SetParent(statsGO.transform, false);
            var stRect = statsTextGO.AddComponent<RectTransform>();
            stRect.anchorMin = new Vector2(0.05f, 0.05f);
            stRect.anchorMax = new Vector2(0.95f, 0.95f);
            stRect.sizeDelta = Vector2.zero;
            m_PlayerStatsText = CreateTMP(statsTextGO, "HEALTH: 100", 15, Color.white, TextAlignmentOptions.Left, FontStyles.Normal);

            // 6. RESPAWN & CLASS SELECTION OVERLAY
            BuildRespawnOverlay(canvasGO);

            // 7. MATCH RESULT OVERLAY
            BuildMatchResultOverlay(canvasGO);
        }

        private void BuildRespawnOverlay(GameObject canvasGO)
        {
            m_RespawnRoot = new GameObject("RespawnOverlay");
            m_RespawnRoot.transform.SetParent(canvasGO.transform, false);
            var respawnRect = m_RespawnRoot.AddComponent<RectTransform>();
            respawnRect.anchorMin = new Vector2(0.5f, 0.5f);
            respawnRect.anchorMax = new Vector2(0.5f, 0.5f);
            respawnRect.pivot = new Vector2(0.5f, 0.5f);
            respawnRect.sizeDelta = new Vector2(680, 420);

            var respawnBg = m_RespawnRoot.AddComponent<UnityEngine.UI.Image>();
            respawnBg.color = new Color(0.08f, 0.10f, 0.15f, 0.96f);

            // Header
            GameObject headerGO = new GameObject("Header");
            headerGO.transform.SetParent(m_RespawnRoot.transform, false);
            var headRect = headerGO.AddComponent<RectTransform>();
            headRect.anchorMin = new Vector2(0f, 0.82f);
            headRect.anchorMax = new Vector2(1f, 1f);
            headRect.sizeDelta = Vector2.zero;
            CreateTMP(headerGO, "TACTICAL RESPAWN & CLASS SELECTION", 22, new Color(0.35f, 0.78f, 1.0f), TextAlignmentOptions.Center, FontStyles.Bold);

            // Timer
            GameObject timerGO = new GameObject("Timer");
            timerGO.transform.SetParent(m_RespawnRoot.transform, false);
            var timeRect = timerGO.AddComponent<RectTransform>();
            timeRect.anchorMin = new Vector2(0f, 0.70f);
            timeRect.anchorMax = new Vector2(1f, 0.82f);
            timeRect.sizeDelta = Vector2.zero;
            m_RespawnTimerText = CreateTMP(timerGO, "Respawning in 5.0 seconds...", 17, Color.yellow, TextAlignmentOptions.Center, FontStyles.Normal);

            // Class selection buttons container
            GameObject classContainer = new GameObject("ClassButtonsRow");
            classContainer.transform.SetParent(m_RespawnRoot.transform, false);
            var cRect = classContainer.AddComponent<RectTransform>();
            cRect.anchorMin = new Vector2(0.04f, 0.38f);
            cRect.anchorMax = new Vector2(0.96f, 0.68f);
            cRect.sizeDelta = Vector2.zero;
            var cLayout = classContainer.AddComponent<HorizontalLayoutGroup>();
            cLayout.spacing = 10;
            cLayout.childAlignment = TextAnchor.MiddleCenter;
            cLayout.childControlWidth = true;
            cLayout.childControlHeight = true;
            m_ClassButtonsContainer = classContainer.transform;

            string[] classNames = new string[] { "Assault\n(100 HP)", "Heavy\n(140 HP)", "Engineer\n(90 HP)", "Recon\n(80 HP)" };
            for (int i = 0; i < 4; i++)
            {
                int classIdx = i;
                GameObject btnGO = new GameObject($"Btn_Class_{i}");
                btnGO.transform.SetParent(m_ClassButtonsContainer, false);
                var btnImg = btnGO.AddComponent<UnityEngine.UI.Image>();
                btnImg.color = new Color(0.2f, 0.25f, 0.35f);
                var btn = btnGO.AddComponent<UnityEngine.UI.Button>();
                btn.targetGraphic = btnImg;
                btn.onClick.AddListener(() =>
                {
                    m_LocalPlayer?.SelectClass(classIdx);
                });
                CreateTMP(btnGO, classNames[i], 15, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
                m_ClassButtons.Add(btn);
            }

            // Spawn point selection buttons container
            GameObject spawnContainer = new GameObject("SpawnPostButtonsRow");
            spawnContainer.transform.SetParent(m_RespawnRoot.transform, false);
            var sRect = spawnContainer.AddComponent<RectTransform>();
            sRect.anchorMin = new Vector2(0.04f, 0.08f);
            sRect.anchorMax = new Vector2(0.96f, 0.32f);
            sRect.sizeDelta = Vector2.zero;
            var sLayout = spawnContainer.AddComponent<HorizontalLayoutGroup>();
            sLayout.spacing = 8;
            sLayout.childAlignment = TextAnchor.MiddleCenter;
            sLayout.childControlWidth = true;
            sLayout.childControlHeight = true;
            m_SpawnPostButtonsContainer = spawnContainer.transform;

            m_RespawnRoot.SetActive(false);
        }

        private void BuildMatchResultOverlay(GameObject canvasGO)
        {
            m_ResultRoot = new GameObject("MatchResultOverlay");
            m_ResultRoot.transform.SetParent(canvasGO.transform, false);
            var resultRect = m_ResultRoot.AddComponent<RectTransform>();
            resultRect.anchorMin = new Vector2(0.5f, 0.5f);
            resultRect.anchorMax = new Vector2(0.5f, 0.5f);
            resultRect.pivot = new Vector2(0.5f, 0.5f);
            resultRect.sizeDelta = new Vector2(560, 240);

            var bg = m_ResultRoot.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color(0.06f, 0.08f, 0.12f, 0.96f);

            GameObject titleGO = new GameObject("ResultTitle");
            titleGO.transform.SetParent(m_ResultRoot.transform, false);
            var tRect = titleGO.AddComponent<RectTransform>();
            tRect.anchorMin = new Vector2(0f, 0.55f);
            tRect.anchorMax = new Vector2(1f, 0.95f);
            tRect.sizeDelta = Vector2.zero;
            m_ResultTitleText = CreateTMP(titleGO, "VICTORY", 42, Color.green, TextAlignmentOptions.Center, FontStyles.Bold);

            GameObject subGO = new GameObject("ResultSub");
            subGO.transform.SetParent(m_ResultRoot.transform, false);
            var sRect = subGO.AddComponent<RectTransform>();
            sRect.anchorMin = new Vector2(0f, 0.25f);
            sRect.anchorMax = new Vector2(1f, 0.55f);
            sRect.sizeDelta = Vector2.zero;
            m_ResultSubText = CreateTMP(subGO, "RED TEAM WON THE BATTLE", 18, Color.white, TextAlignmentOptions.Center, FontStyles.Normal);

            m_ResultRoot.SetActive(false);
        }

        private TextMeshProUGUI CreateTMP(GameObject parent, string text, float fontSize, Color color, TextAlignmentOptions align, FontStyles style)
        {
            var tmp = parent.AddComponent<TextMeshProUGUI>();
            tmp.font = m_Font;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.fontStyle = style;
            return tmp;
        }

        private void Update()
        {
            FindLocalPlayer();

            if (NetworkManager.Singleton == null || (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer))
            {
                if (m_HUDCanvas != null) m_HUDCanvas.enabled = false;
                return;
            }

            if (m_HUDCanvas != null) m_HUDCanvas.enabled = true;

            UpdateTicketBar();
            UpdateCommandPosts();
            UpdateCaptureProgress();
            UpdateCrosshair();
            UpdatePlayerStats();
            UpdateRespawnScreen();
            UpdateMatchResult();
        }

        private void UpdateTicketBar()
        {
            var mm = ConquestMatchManager.Instance;
            if (mm == null) return;

            if (m_RedTicketsText != null) m_RedTicketsText.text = $"RED: {mm.RedTickets}";
            if (m_BlueTicketsText != null) m_BlueTicketsText.text = $"BLUE: {mm.BlueTickets}";
        }

        private void UpdateCommandPosts()
        {
            var mm = ConquestMatchManager.Instance;
            if (mm == null || mm.CommandPosts == null) return;

            var posts = mm.CommandPosts;
            while (m_PostElements.Count < posts.Count)
            {
                int index = m_PostElements.Count;
                GameObject postGO = new GameObject($"Post_{index}");
                postGO.transform.SetParent(m_PostsContainer, false);
                var rt = postGO.AddComponent<RectTransform>();
                rt.sizeDelta = new Vector2(95, 34);

                var img = postGO.AddComponent<UnityEngine.UI.Image>();
                img.color = new Color(0.3f, 0.3f, 0.3f, 0.9f);
                var tmp = CreateTMP(postGO, "Post", 14, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);

                m_PostElements.Add(postGO);
                m_PostImages.Add(img);
                m_PostTexts.Add(tmp);
            }

            for (int i = 0; i < m_PostElements.Count; i++)
            {
                if (i < posts.Count && posts[i] != null)
                {
                    m_PostElements[i].SetActive(true);
                    var post = posts[i];

                    Color col = new Color(0.4f, 0.4f, 0.45f, 0.9f);
                    if (post.CurrentOwner == Team.Red) col = new Color(0.85f, 0.2f, 0.2f, 0.9f);
                    else if (post.CurrentOwner == Team.Blue) col = new Color(0.2f, 0.5f, 0.9f, 0.9f);

                    if (post.IsContested) col = new Color(0.9f, 0.75f, 0.1f, 0.95f);

                    m_PostImages[i].color = col;

                    string label = post.PostName;
                    if (post.IsContested) label += " (!)";
                    else if (post.CaptureProgress > 0f && post.CaptureProgress < 1f)
                    {
                        label += $" {(int)(post.CaptureProgress * 100)}%";
                    }
                    m_PostTexts[i].text = label;
                }
                else
                {
                    m_PostElements[i].SetActive(false);
                }
            }
        }

        private void UpdateCaptureProgress()
        {
            if (m_LocalPlayer == null || m_LocalPlayer.Health.IsDead)
            {
                if (m_CaptureBarRoot != null) m_CaptureBarRoot.SetActive(false);
                return;
            }

            var mm = ConquestMatchManager.Instance;
            if (mm == null) return;

            Vector3 playerPos = m_LocalPlayer.transform.position;
            ConquestCommandPost nearbyPost = null;

            foreach (var post in mm.CommandPosts)
            {
                if (post != null && Vector3.Distance(post.transform.position, playerPos) <= post.CaptureRadius)
                {
                    nearbyPost = post;
                    break;
                }
            }

            if (nearbyPost == null)
            {
                if (m_CaptureBarRoot != null) m_CaptureBarRoot.SetActive(false);
                return;
            }

            if (m_CaptureBarRoot != null) m_CaptureBarRoot.SetActive(true);

            float progress = Mathf.Clamp01(nearbyPost.CaptureProgress);
            Team capTeam = nearbyPost.capturingTeam.Value;
            Color barCol = capTeam == Team.Red ? new Color(0.9f, 0.2f, 0.2f) : (capTeam == Team.Blue ? new Color(0.2f, 0.5f, 0.9f) : Color.white);
            if (nearbyPost.IsContested) barCol = Color.yellow;

            if (m_CaptureBarFill != null)
            {
                m_CaptureBarFill.color = barCol;
                var rt = m_CaptureBarFill.GetComponent<RectTransform>();
                rt.anchorMax = new Vector2(progress, 1);
            }

            if (m_CaptureStatusText != null)
            {
                m_CaptureStatusText.text = nearbyPost.IsContested
                    ? $"[CONTESTED] {nearbyPost.PostName}"
                    : $"Capturing {nearbyPost.PostName} ({(int)(progress * 100)}%)";
            }
        }

        private void UpdateCrosshair()
        {
            if (m_CrosshairText == null) return;

            if (m_LocalPlayer == null || m_LocalPlayer.Health.IsDead)
            {
                m_CrosshairText.gameObject.SetActive(false);
                return;
            }

            m_CrosshairText.gameObject.SetActive(true);

            if (Time.time < m_HitmarkerEndTime)
            {
                m_CrosshairText.color = Color.red;
                m_CrosshairText.text = "X";
            }
            else
            {
                m_CrosshairText.color = new Color(1f, 1f, 1f, 0.8f);
                m_CrosshairText.text = "+";
            }
        }

        private void UpdatePlayerStats()
        {
            if (m_LocalPlayer == null || m_PlayerStatsText == null) return;

            if (m_LocalPlayer.Health.IsDead)
            {
                m_PlayerStatsBg.gameObject.SetActive(false);
                return;
            }

            m_PlayerStatsBg.gameObject.SetActive(true);
            m_PlayerStatsBg.color = m_LocalPlayer.CurrentTeam == Team.Red
                ? new Color(0.7f, 0.15f, 0.15f, 0.85f)
                : new Color(0.15f, 0.35f, 0.85f, 0.85f);

            string className = "Standard";
            if (m_LocalPlayer.AvailableClasses != null && m_LocalPlayer.CurrentClassIndex >= 0 && m_LocalPlayer.CurrentClassIndex < m_LocalPlayer.AvailableClasses.Count)
            {
                className = m_LocalPlayer.AvailableClasses[m_LocalPlayer.CurrentClassIndex].className;
            }

            string ammoText = "";
            var wep = m_LocalPlayer.Weapon;
            if (wep != null)
            {
                ammoText = wep.IsReloading ? "\nAMMO: [RELOADING...]" : $"\nAMMO: {wep.CurrentAmmo} / {wep.MagazineCapacity} [R]";
                if (wep.HasSupportStation) ammoText += " | [Q] Station";
                if (wep.HasZoom) ammoText += " | [RMB] Scope";
            }

            m_PlayerStatsText.text = $"<b>TEAM:</b> {m_LocalPlayer.CurrentTeam}  |  <b>CLASS:</b> {className}\n<b>HEALTH:</b> {m_LocalPlayer.Health.CurrentHealth} / {m_LocalPlayer.Health.MaxHealth}{ammoText}";
        }

        private void UpdateRespawnScreen()
        {
            if (m_RespawnRoot == null) return;

            if (m_LocalPlayer == null || !m_LocalPlayer.Health.IsDead)
            {
                m_RespawnRoot.SetActive(false);
                return;
            }

            m_RespawnRoot.SetActive(true);
            if (m_RespawnTimerText != null)
            {
                m_RespawnTimerText.text = $"Respawning in {m_LocalPlayer.RemainingRespawnTime:0.0} seconds...";
            }

            // Highlight selected class
            for (int i = 0; i < m_ClassButtons.Count; i++)
            {
                var img = m_ClassButtons[i].GetComponent<UnityEngine.UI.Image>();
                if (img != null)
                {
                    img.color = m_LocalPlayer.CurrentClassIndex == i
                        ? new Color(0.2f, 0.8f, 0.35f)
                        : new Color(0.25f, 0.30f, 0.40f);
                }
            }

            // Refresh spawn posts buttons
            var mm = ConquestMatchManager.Instance;
            if (mm != null && m_SpawnPostButtonsContainer != null)
            {
                var friendlyPosts = mm.GetFriendlyPostNames(m_LocalPlayer.CurrentTeam);
                while (m_SpawnPostButtons.Count < friendlyPosts.Count)
                {
                    GameObject btnGO = new GameObject($"SpawnBtn_{m_SpawnPostButtons.Count}");
                    btnGO.transform.SetParent(m_SpawnPostButtonsContainer, false);
                    var img = btnGO.AddComponent<UnityEngine.UI.Image>();
                    var btn = btnGO.AddComponent<UnityEngine.UI.Button>();
                    btn.targetGraphic = img;
                    CreateTMP(btnGO, "Post", 14, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
                    m_SpawnPostButtons.Add(btn);
                }

                for (int i = 0; i < m_SpawnPostButtons.Count; i++)
                {
                    if (i < friendlyPosts.Count)
                    {
                        m_SpawnPostButtons[i].gameObject.SetActive(true);
                        string postName = friendlyPosts[i];
                        bool isSelected = m_LocalPlayer.SelectedPostName == postName;
                        var img = m_SpawnPostButtons[i].GetComponent<UnityEngine.UI.Image>();
                        img.color = isSelected ? Color.cyan : new Color(0.3f, 0.35f, 0.45f);

                        var tmp = m_SpawnPostButtons[i].GetComponentInChildren<TextMeshProUGUI>();
                        if (tmp != null) tmp.text = isSelected ? $"> Post {postName} <" : $"Post {postName}";

                        m_SpawnPostButtons[i].onClick.RemoveAllListeners();
                        m_SpawnPostButtons[i].onClick.AddListener(() =>
                        {
                            m_LocalPlayer?.SelectSpawnPost(postName);
                        });
                    }
                    else
                    {
                        m_SpawnPostButtons[i].gameObject.SetActive(false);
                    }
                }
            }
        }

        private void UpdateMatchResult()
        {
            if (m_ResultRoot == null) return;

            var mm = ConquestMatchManager.Instance;
            if (mm == null || !mm.IsMatchOver)
            {
                m_ResultRoot.SetActive(false);
                return;
            }

            m_ResultRoot.SetActive(true);

            bool isWinner = m_LocalPlayer != null && m_LocalPlayer.CurrentTeam == mm.WinningTeam;
            if (m_ResultTitleText != null)
            {
                m_ResultTitleText.text = isWinner ? "VICTORY!" : "DEFEAT";
                m_ResultTitleText.color = isWinner ? Color.green : Color.red;
            }

            if (m_ResultSubText != null)
            {
                m_ResultSubText.text = $"{mm.WinningTeam} TEAM WON THE BATTLE!";
            }
        }
    }
}

