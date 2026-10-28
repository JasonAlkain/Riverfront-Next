using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.Conquest.Gameplay;
using Game.Conquest.Lobby;
using Game.Conquest.Networking;
using Game.Conquest.Player;

namespace Game.Conquest.NetTest
{
    /// <summary>
    /// Development-only multi-instance test driver. Started by <c>-e2e</c> on the command line, it plays
    /// one peer of a networked match by itself (host or join, ready up, deploy, redeploy, leave) and
    /// writes greppable <c>[E2E]</c> lines to the player log, plus bandwidth and frame-time samples.
    ///
    /// It only calls the same client entry points the menus and HUD use (session service, lobby RPCs,
    /// ConquestPlayer.Deploy), so it adds no network messages and can't do anything a player can't.
    /// The assembly only compiles in the Editor and development builds. See Tools/NetTest/README.md.
    /// </summary>
    public sealed class ConquestNetTestDriver : MonoBehaviour
    {
        private sealed class Options
        {
            public string name = "";
            public bool host;
            public bool hostOnline;
            public string join = "";
            public string joinCode = "";
            public string browse = "";
            public string map = "";
            public string sessionName = "E2E Battle";
            public int tickets = 30;
            public int teamSize = 4;
            public bool bots = true;
            public int maxPlayers = 16;
            public int startWhen = 1;
            public int classIndex = -1;
            public bool switchTeam;
            public int changeClassAtPost = -1;
            public bool noDeploy;
            public float leaveAfter = -1f;
            public float quitAfter = -1f;
            public int matches = 1;
            public string netSim = "";
            public string authProfile = "";
            public float statsEvery = 5f;
            public bool uncapFps;
            public float joinDelay = 2f;
        }

        private static Options s_Options;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (!HasFlag(args, "-e2e")) return;
            s_Options = Parse(args);
            var go = new GameObject("[E2E Test Driver]");
            DontDestroyOnLoad(go);
            go.AddComponent<ConquestNetTestDriver>();
        }

        // ---------- State ----------

        private float m_StartTime;
        private string m_Scene = "";
        private bool m_ActionTaken;          // host started / join started
        private float m_MenuReadyAt = -1f;
        private bool m_ConnectedLogged;
        private bool m_ClassPicked;
        private bool m_TeamSwitched;
        private float m_NextReadyTry;
        private int m_ResultsSeen;
        private bool m_WasDead = true;
        private bool m_EverDeployed;
        private float m_FirstDeployTime = -1f;
        private float m_NextDeployTry;
        private int m_Deploys;
        private int m_Deaths;
        private int m_Kills;
        private int m_KillReports;
        private bool m_ChangeClassDone;
        private float m_ChangeClassCheckAt = -1f;
        private bool m_LeftSession;
        private bool m_Quitting;
        private bool m_MatchOverLogged;
        private int m_LastRed = -1, m_LastBlue = -1;
        private int m_LastLobbyCount = -1;
        private int m_LastCountdown = -2;
        private NetworkManager m_SubscribedNm;
        private ConquestSessionManager m_SubscribedSession;
        private bool m_NetSimApplied;
        private int m_LastResultsMatchId;
        private readonly Dictionary<string, int> m_PostStates = new Dictionary<string, int>();
        private float m_NextFireInputLog;

        // ---------- Stats ----------

        private ProfilerRecorder m_BytesSent;
        private ProfilerRecorder m_BytesReceived;
        private ProfilerRecorder m_MainThread;
        private ProfilerRecorder m_GcAlloc;
        private ProfilerRecorder m_BotUpdate;
        private ProfilerRecorder m_BotPerception;
        private ProfilerRecorder m_BoltSim;
        private readonly List<float> m_GcKB = new List<float>(1024);
        // Frame time minus the frame-rate cap's wait: what the frame actually cost.
        private ProfilerRecorder m_PlayerLoop;
        private ProfilerRecorder m_WaitForFps;
        private readonly List<float> m_BusyMs = new List<float>(1024);
        private double m_BotUpdateMs, m_BotPerceptionMs, m_BoltSimMs;
        private long m_WindowSent, m_WindowReceived;
        private readonly List<float> m_FrameMs = new List<float>(1024);
        private readonly List<float> m_MainThreadMs = new List<float>(1024);
        private float m_WindowStart;
        private int m_LastTick;
        private int m_WindowStartTick;

        private void Start()
        {
            m_StartTime = Time.realtimeSinceStartup;
            Log($"driver up: {string.Join(" ", Environment.GetCommandLineArgs(), 1, Environment.GetCommandLineArgs().Length - 1)}");
            Log($"version {ConquestSessionManager.BuildVersion} dev={Debug.isDebugBuild}");

            if (!string.IsNullOrEmpty(s_Options.name)) ConquestSessionManager.SaveLocalPlayerName(s_Options.name);
            if (!string.IsNullOrEmpty(s_Options.authProfile)) InitServicesWithProfile(s_Options.authProfile);

            SceneManager.sceneLoaded += OnSceneLoaded;
            ConquestMatchManager.KillReported += OnKillReported;

            m_BytesSent = ProfilerRecorder.StartNew(ProfilerCategory.Network, "Total Bytes Sent");
            m_BytesReceived = ProfilerRecorder.StartNew(ProfilerCategory.Network, "Total Bytes Received");
            m_MainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            m_GcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            m_BotUpdate = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Conquest.BotUpdate");
            m_BotPerception = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Conquest.BotPerception");
            m_BoltSim = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Conquest.BoltSim");
            m_PlayerLoop = StartByName("PlayerLoop");
            m_WaitForFps = StartByName("WaitForTargetFPS");
            m_WindowStart = Time.realtimeSinceStartup;
            Log($"recorders sent={m_BytesSent.Valid} received={m_BytesReceived.Valid} mainThread={m_MainThread.Valid} playerLoop={m_PlayerLoop.Valid} waitForFps={m_WaitForFps.Valid}");
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            ConquestMatchManager.KillReported -= OnKillReported;
            m_BytesSent.Dispose();
            m_BytesReceived.Dispose();
            m_MainThread.Dispose();
            m_GcAlloc.Dispose();
            m_BotUpdate.Dispose();
            m_BotPerception.Dispose();
            m_BoltSim.Dispose();
            m_PlayerLoop.Dispose();
            m_WaitForFps.Dispose();
        }

        // Instances on one PC share the anonymous sign-in cache; a profile per instance gives each its own player id.
        private static async void InitServicesWithProfile(string profile)
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Uninitialized) return;
                await UnityServices.InitializeAsync(new InitializationOptions().SetProfile(profile));
                Log($"services initialised with profile {profile}");
            }
            catch (Exception e)
            {
                Log($"services init FAILED: {e.Message}");
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            m_Scene = scene.name;
            Log($"scene {scene.name}");
            if (scene.name == "MainMenu") m_MenuReadyAt = Time.realtimeSinceStartup + s_Options.joinDelay;
            m_MatchOverLogged = false;
            m_PostStates.Clear();
            m_LastRed = m_LastBlue = -1;
            m_LastLobbyCount = -1;
            m_LastCountdown = -2;
            m_ClassPicked = false;
            m_TeamSwitched = false;
            if (s_Options.uncapFps) Application.targetFrameRate = -1;
        }

        private void OnKillReported(KillReport report)
        {
            m_KillReports++;
            var local = ConquestPlayer.Local;
            if (local != null && report.hasKiller && report.killerName.ToString() == local.playerName.Value.ToString())
            {
                m_Kills++;
                Log($"kill feed: I killed {report.victimName} with {report.weaponName}");
            }
        }

        private void Update()
        {
            SampleStats();
            if (m_Quitting) return;

            var nm = NetworkManager.Singleton;
            HookNetworkManager(nm);
            var sessionManager = ConquestSessionManager.Instance;
            if (sessionManager != m_SubscribedSession)
            {
                if (m_SubscribedSession != null) m_SubscribedSession.OnMatchResultsAvailable -= OnResults;
                m_SubscribedSession = sessionManager;
                if (sessionManager != null) sessionManager.OnMatchResultsAvailable += OnResults;
            }

            float elapsed = Time.realtimeSinceStartup - m_StartTime;
            if (s_Options.quitAfter > 0f && elapsed > s_Options.quitAfter)
            {
                Quit("quit-after reached");
                return;
            }

            if (m_Scene == "MainMenu") UpdateMenu(nm);
            UpdateLobby(nm);
            UpdateMatch(nm);
        }

        // ---------- Menu: host or join ----------

        private void UpdateMenu(NetworkManager nm)
        {
            if (m_ActionTaken || m_LeftSession || m_MenuReadyAt < 0f || Time.realtimeSinceStartup < m_MenuReadyAt) return;
            var session = ConquestSession.Current;
            if (session == null || nm == null) return;
            if (ConquestSessionManager.Instance != null && ConquestSessionManager.Instance.isDedicatedServer)
            {
                m_ActionTaken = true;
                Log("role dedicated-server");
                return;
            }
            m_ActionTaken = true;
            ApplyNetSimOnce(nm);

            var o = s_Options;
            if (o.host || o.hostOnline)
            {
                if (o.hostOnline)
                {
                    Log($"hosting online map={o.map} tickets={o.tickets} teamSize={o.teamSize} bots={o.bots}");
                    session.StartOnlineHostGame(o.map, "", o.sessionName, o.maxPlayers, o.tickets, o.teamSize, o.bots, BotDifficulty.Normal,
                        (ok, msg) => Log(ok ? $"host online OK: {msg}" : $"host online FAILED: {msg}"));
                }
                else
                {
                    bool ok = session.StartHostGame(o.map, "", o.sessionName, o.maxPlayers, o.tickets, o.teamSize, o.bots, BotDifficulty.Normal);
                    Log(ok ? $"host LAN OK: {session.StatusMessage}" : "host LAN FAILED");
                }
                return;
            }

            session.OnOnlineJoinFailed -= OnOnlineJoinFailed;
            session.OnOnlineJoinFailed += OnOnlineJoinFailed;

            if (!string.IsNullOrEmpty(o.joinCode))
            {
                Log($"joining by code {o.joinCode}");
                session.StartClientConnect(ConquestConnectionData.ForJoinCode(o.joinCode));
            }
            else if (!string.IsNullOrEmpty(o.browse))
            {
                Log($"browsing for '{o.browse}'");
                session.QueryOnlineMatches((list, error) =>
                {
                    if (error != null) { Log($"browse FAILED: {error}"); return; }
                    Log($"browse found {list.Count} match(es)");
                    foreach (var m in list)
                    {
                        Log($"  listed '{m.name}' map={m.map} {m.players}/{m.maxPlayers}");
                        if (m.name == o.browse)
                        {
                            Log($"joining listed match by its code {m.joinCode}");
                            session.StartClientConnect(ConquestConnectionData.ForJoinCode(m.joinCode));
                            return;
                        }
                    }
                    Log("browse: match not in the list; retrying");
                    m_ActionTaken = false;
                    m_MenuReadyAt = Time.realtimeSinceStartup + 5f;
                });
            }
            else if (!string.IsNullOrEmpty(o.join))
            {
                string address = o.join;
                ushort port = ConquestSessionManager.DefaultPort;
                int colon = address.LastIndexOf(':');
                if (colon > 0 && ushort.TryParse(address.Substring(colon + 1), out var p)) { port = p; address = address.Substring(0, colon); }
                Log($"joining LAN {address}:{port}");
                bool ok = session.StartClientConnect(new ConquestConnectionData(address, port));
                if (!ok) Log("join LAN FAILED to start");
            }
            else
            {
                Log("no role given (-e2eHost, -e2eJoin, -e2eJoinCode or -e2eBrowse); idle");
            }
        }

        private void OnOnlineJoinFailed(string reason)
        {
            Log($"online join FAILED: {reason}");
        }

        private void HookNetworkManager(NetworkManager nm)
        {
            if (nm == m_SubscribedNm) return;
            if (m_SubscribedNm != null)
            {
                m_SubscribedNm.OnClientConnectedCallback -= OnClientConnected;
                m_SubscribedNm.OnClientDisconnectCallback -= OnClientDisconnected;
                m_SubscribedNm.OnTransportFailure -= OnTransportFailure;
            }
            m_SubscribedNm = nm;
            if (nm == null) return;
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
            nm.OnTransportFailure += OnTransportFailure;
        }

        private void OnClientConnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm.IsServer) Log($"server: client {clientId} connected (clients={nm.ConnectedClientsIds.Count})");
            else if (clientId == nm.LocalClientId) { Log($"connected as client {clientId}"); m_ConnectedLogged = true; }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            if (nm.IsServer) Log($"server: client {clientId} disconnected");
            else Log($"disconnected (reason='{nm.DisconnectReason}')");
        }

        private void OnTransportFailure()
        {
            Log("TRANSPORT FAILURE");
        }

        private void ApplyNetSimOnce(NetworkManager nm)
        {
            if (m_NetSimApplied || string.IsNullOrEmpty(s_Options.netSim) || nm == null) return;
            m_NetSimApplied = true;
            string[] parts = s_Options.netSim.Split(',');
            int delay = parts.Length > 0 ? ParseInt(parts[0]) : 0;
            int jitter = parts.Length > 1 ? ParseInt(parts[1]) : 0;
            int loss = parts.Length > 2 ? ParseInt(parts[2]) : 0;
            var sim = nm.GetComponent<NetworkSimulator>();
            if (sim == null) sim = nm.gameObject.AddComponent<NetworkSimulator>();
            sim.ConnectionPreset = NetworkSimulatorPreset.Create("E2E", "", delay, jitter, 0, loss);
            Log($"network simulator: delay={delay}ms jitter={jitter}ms loss={loss}%");
        }

        // ---------- Lobby ----------

        private void UpdateLobby(NetworkManager nm)
        {
            var lobby = ConquestLobbyManager.Instance;
            if (lobby == null || !lobby.IsSpawned || nm == null) return;

            int count = lobby.lobbyPlayers.Count;
            if (count != m_LastLobbyCount)
            {
                m_LastLobbyCount = count;
                var sb = new StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    var p = lobby.lobbyPlayers[i];
                    sb.Append($" [{p.clientId}:{p.playerName} {p.team} class{p.classIndex} ready={p.isReady}{(p.isHost ? " host" : "")}]");
                }
                Log($"lobby '{lobby.sessionName.Value}' map={lobby.selectedMapScene.Value} tickets={lobby.reinforcementTickets.Value} teamSize={lobby.targetTeamSize.Value} bots={lobby.enableBotFilling.Value} players={count}:{sb}");
            }
            if (lobby.autoStartCountdown.Value != m_LastCountdown)
            {
                m_LastCountdown = lobby.autoStartCountdown.Value;
                if (m_LastCountdown >= 0) Log($"lobby countdown {m_LastCountdown}");
            }

            if (!nm.IsClient) return; // dedicated server: nothing to press
            if (m_ResultsSeen >= s_Options.matches) return; // done: stay in the lobby unready

            int mine = -1;
            for (int i = 0; i < count; i++) if (lobby.lobbyPlayers[i].clientId == nm.LocalClientId) mine = i;
            if (mine < 0) return;
            var me = lobby.lobbyPlayers[mine];

            if (s_Options.switchTeam && !m_TeamSwitched)
            {
                m_TeamSwitched = true;
                Log($"switching team from {me.team}");
                lobby.SwitchTeamServerRpc();
                m_NextReadyTry = Time.realtimeSinceStartup + 1f;
                return;
            }
            if (s_Options.classIndex >= 0 && !m_ClassPicked)
            {
                m_ClassPicked = true;
                Log($"picking class {s_Options.classIndex}");
                lobby.SelectClassServerRpc(s_Options.classIndex);
                m_NextReadyTry = Time.realtimeSinceStartup + 1f;
                return;
            }

            bool hostWaiting = nm.IsHost && count < s_Options.startWhen;
            if (!me.isReady && !hostWaiting && Time.realtimeSinceStartup >= m_NextReadyTry)
            {
                m_NextReadyTry = Time.realtimeSinceStartup + 2f;
                Log("ready");
                lobby.ToggleReadyServerRpc();
            }
        }

        // ---------- Match ----------

        private void UpdateMatch(NetworkManager nm)
        {
            var match = ConquestMatchManager.Instance;
            if (match == null || !match.IsSpawned || nm == null) return;

            if (match.RedTickets != m_LastRed || match.BlueTickets != m_LastBlue)
            {
                // Only every 5 tickets, plus the last few, to keep the log readable.
                bool log = m_LastRed < 0 || match.RedTickets / 5 != m_LastRed / 5 || match.BlueTickets / 5 != m_LastBlue / 5 ||
                           match.RedTickets < 5 || match.BlueTickets < 5;
                m_LastRed = match.RedTickets;
                m_LastBlue = match.BlueTickets;
                if (log) Log($"tickets red={m_LastRed} blue={m_LastBlue}");
            }

            // Post ownership and contest changes as this peer sees them (server and clients should agree).
            var posts = match.CommandPosts;
            for (int i = 0; i < posts.Count; i++)
            {
                var post = posts[i];
                if (post == null || !post.IsCapturable) continue;
                int state = (int)post.CurrentOwner * 2 + (post.IsContested ? 1 : 0);
                if (!m_PostStates.TryGetValue(post.PostName, out int previous) || previous != state)
                {
                    m_PostStates[post.PostName] = state;
                    Log($"post {post.PostName} owner={post.CurrentOwner} contested={post.IsContested}");
                }
            }

            if (match.IsMatchOver && !m_MatchOverLogged)
            {
                m_MatchOverLogged = true;
                Log($"MATCH OVER winner={match.WinningTeam} red={match.RedTickets} blue={match.BlueTickets} scoreboardRows={match.liveStats.Count} killFeed={m_KillReports}");
            }

            var local = ConquestPlayer.Local;
            if (local == null || local.Health == null || !nm.IsClient) return;

            // The driver never fires; this shows when a person's mouse or keys reached a test window.
            if (Game.Conquest.Controls.ConquestInput.FireHeld && Time.realtimeSinceStartup >= m_NextFireInputLog)
            {
                m_NextFireInputLog = Time.realtimeSinceStartup + 5f;
                Log("fire input received from a real device (window focused?)");
            }

            bool dead = local.Health.IsDead;
            if (dead != m_WasDead)
            {
                if (dead)
                {
                    m_Deaths++;
                    Log($"died (deaths={m_Deaths}) team={local.CurrentTeam} tickets red={match.RedTickets} blue={match.BlueTickets}");
                }
                else
                {
                    m_Deploys++;
                    if (!m_EverDeployed) { m_EverDeployed = true; m_FirstDeployTime = Time.realtimeSinceStartup; }
                    var cls = local.CurrentClassData;
                    Log($"deployed #{m_Deploys} as {(cls != null ? cls.name : local.CurrentClassIndex.ToString())} team={local.CurrentTeam} at {local.transform.position} tickets red={match.RedTickets} blue={match.BlueTickets}");
                    if (s_Options.changeClassAtPost >= 0 && !m_ChangeClassDone) m_ChangeClassCheckAt = Time.realtimeSinceStartup + 1.5f;
                }
                m_WasDead = dead;
            }

            if (dead && !s_Options.noDeploy && !match.IsMatchOver && Time.realtimeSinceStartup >= m_NextDeployTry
                && local.RemainingRespawnTime <= 0f && local.HasTicketForDeploy)
            {
                m_NextDeployTry = Time.realtimeSinceStartup + 1f;
                int cls = s_Options.classIndex >= 0 ? s_Options.classIndex : local.PendingClassIndex;
                local.Deploy(cls, "");
            }

            if (m_ChangeClassCheckAt > 0f && Time.realtimeSinceStartup >= m_ChangeClassCheckAt && !dead)
            {
                m_ChangeClassCheckAt = -1f;
                var post = local.FriendlyPostHere();
                if (post == null)
                {
                    Log("change class: not standing in a friendly post; will try after the next deploy");
                }
                else
                {
                    m_ChangeClassDone = true;
                    int before = local.CurrentClassIndex;
                    Log($"change class at post {post.PostName}: {before} -> {s_Options.changeClassAtPost}");
                    local.ChangeClassAtPost(s_Options.changeClassAtPost);
                    StartCoroutine(CheckClassChanged(local, before));
                }
            }

            if (s_Options.leaveAfter > 0f && m_FirstDeployTime > 0f && !m_LeftSession &&
                Time.realtimeSinceStartup - m_FirstDeployTime > s_Options.leaveAfter)
            {
                m_LeftSession = true;
                Log("leaving the session mid-match");
                ConquestSession.Current?.LeaveSession();
            }
        }

        private System.Collections.IEnumerator CheckClassChanged(ConquestPlayer local, int before)
        {
            yield return new WaitForSecondsRealtime(1.5f);
            if (local == null) yield break;
            Log(local.CurrentClassIndex != before
                ? $"change class OK: now class {local.CurrentClassIndex} health={local.Health.CurrentHealth}/{local.Health.MaxHealth}"
                : $"change class FAILED: still class {before}");
        }

        private void OnResults(MatchResults results)
        {
            // Count each match once even if its results arrive more than once.
            bool repeat = results.matchId == m_LastResultsMatchId && m_LastResultsMatchId != 0;
            m_LastResultsMatchId = results.matchId;
            if (repeat)
            {
                Log($"DUPLICATE results for match {results.matchId}");
                return;
            }
            m_ResultsSeen++;
            var nm = NetworkManager.Singleton;
            ulong me = nm != null ? nm.LocalClientId : ulong.MaxValue;
            int myRow = -1;
            for (int i = 0; i < results.rows.Count; i++) if (results.rows[i].clientId == me && !results.rows[i].isBot) myRow = i;
            Log($"results #{m_ResultsSeen}: winner={results.winner} rows={results.rows.Count} myRow={(myRow >= 0 ? $"k{results.rows[myRow].kills}/d{results.rows[myRow].deaths}" : "none")} duration={results.durationSeconds:0}s");
            var profile = Game.Conquest.Profile.ProfileStore.Current;
            Log($"profile now matches={profile.matchesPlayed} kills={profile.kills} deaths={profile.deaths}");
            m_EverDeployed = false;
            m_ChangeClassDone = false;
            if (m_ResultsSeen >= s_Options.matches && s_Options.quitAfter <= 0f)
            {
                Log("all matches played; quitting in 25 s (after the lobby returns)");
                s_Options.quitAfter = Time.realtimeSinceStartup - m_StartTime + 25f;
            }
        }

        private void Quit(string why)
        {
            m_Quitting = true;
            Log($"SUMMARY deploys={m_Deploys} deaths={m_Deaths} kills={m_Kills} killFeed={m_KillReports} results={m_ResultsSeen} scene={m_Scene}");
            Log($"quitting: {why}");
            Application.Quit(0);
        }

        // ---------- Stats ----------

        private void SampleStats()
        {
            if (m_BytesSent.Valid) m_WindowSent += m_BytesSent.LastValue;
            if (m_BytesReceived.Valid) m_WindowReceived += m_BytesReceived.LastValue;
            m_FrameMs.Add(Time.unscaledDeltaTime * 1000f);
            if (m_MainThread.Valid) m_MainThreadMs.Add(m_MainThread.LastValue / 1_000_000f);
            if (m_GcAlloc.Valid) m_GcKB.Add(m_GcAlloc.LastValue / 1024f);
            if (m_BotUpdate.Valid) m_BotUpdateMs += m_BotUpdate.LastValue / 1e6;
            if (m_BotPerception.Valid) m_BotPerceptionMs += m_BotPerception.LastValue / 1e6;
            if (m_BoltSim.Valid) m_BoltSimMs += m_BoltSim.LastValue / 1e6;
            if (m_PlayerLoop.Valid)
            {
                long wait = m_WaitForFps.Valid ? m_WaitForFps.LastValue : 0;
                m_BusyMs.Add((m_PlayerLoop.LastValue - wait) / 1_000_000f);
            }

            float now = Time.realtimeSinceStartup;
            float window = now - m_WindowStart;
            if (s_Options.statsEvery <= 0f || window < s_Options.statsEvery) return;

            var nm = NetworkManager.Singleton;
            int tick = nm != null && nm.IsListening ? nm.LocalTime.Tick : 0;
            float ticksPerSecond = m_WindowStartTick > 0 && tick > 0 ? (tick - m_WindowStartTick) / window : 0f;
            m_WindowStartTick = tick;

            if (nm != null && nm.IsListening)
            {
                string role = nm.IsHost ? "host" : nm.IsServer ? "server" : "client";
                int combatants = ConquestPlayer.All.Count;
                int bots = 0;
                foreach (var p in ConquestPlayer.All) if (p != null && p.isBot) bots++;
                string rtt = "";
                if (!nm.IsServer)
                {
                    var transport = nm.NetworkConfig.NetworkTransport;
                    if (transport != null) rtt = $" rtt={transport.GetCurrentRtt(NetworkManager.ServerClientId)}ms";
                }
                int clients = nm.IsServer ? nm.ConnectedClientsIds.Count : 0;
                Log(string.Format(CultureInfo.InvariantCulture,
                    "STATS role={0} scene={1} combatants={2} bots={3} clients={4} sentKBps={5:0.0} recvKBps={6:0.0} frameMs p50={7:0.0} p95={8:0.0} max={9:0.0} mainThreadMs p50={10:0.0} p95={11:0.0} ticksPerSec={12:0.0}{13} monoMB={14:0.0}",
                    role, m_Scene, combatants, bots, clients,
                    m_WindowSent / 1024f / window, m_WindowReceived / 1024f / window,
                    Percentile(m_FrameMs, 0.5f), Percentile(m_FrameMs, 0.95f), Percentile(m_FrameMs, 1f),
                    Percentile(m_MainThreadMs, 0.5f), Percentile(m_MainThreadMs, 0.95f), ticksPerSecond, rtt,
                    UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / (1024f * 1024f)));
                int frames = Mathf.Max(1, m_FrameMs.Count);
                Log(string.Format(CultureInfo.InvariantCulture,
                    "PERF gcKBPerFrame p50={0:0.00} p95={1:0.00} max={2:0.00} gcKBPerSec={3:0.0} busyMs p50={4:0.00} p95={5:0.00} max={6:0.00} msPerSec botUpdate={7:0.00} botPerception={8:0.000} boltSim={9:0.00} fps={10:0}",
                    Percentile(m_GcKB, 0.5f), Percentile(m_GcKB, 0.95f), Percentile(m_GcKB, 1f), Sum(m_GcKB) / window,
                    Percentile(m_BusyMs, 0.5f), Percentile(m_BusyMs, 0.95f), Percentile(m_BusyMs, 1f),
                    m_BotUpdateMs / window, m_BotPerceptionMs / window, m_BoltSimMs / window, frames / window));
            }
            m_GcKB.Clear();
            m_BusyMs.Clear();
            m_BotUpdateMs = m_BotPerceptionMs = m_BoltSimMs = 0;

            m_WindowSent = m_WindowReceived = 0;
            m_FrameMs.Clear();
            m_MainThreadMs.Clear();
            m_WindowStart = now;
        }

        private static float Sum(List<float> values)
        {
            float total = 0f;
            for (int i = 0; i < values.Count; i++) total += values[i];
            return total;
        }

        // Engine markers live in categories that vary between versions; find them by name.
        private static ProfilerRecorder StartByName(string markerName)
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var handle in handles)
            {
                if (ProfilerRecorderHandle.GetDescription(handle).Name == markerName)
                {
                    var recorder = new ProfilerRecorder(handle, 1, ProfilerRecorderOptions.Default);
                    recorder.Start();
                    return recorder;
                }
            }
            Log($"profiler marker '{markerName}' not found");
            return default;
        }

        private static float Percentile(List<float> values, float p)
        {
            if (values.Count == 0) return 0f;
            values.Sort();
            int i = Mathf.Clamp(Mathf.CeilToInt(p * values.Count) - 1, 0, values.Count - 1);
            return values[i];
        }

        // ---------- Helpers ----------

        private static void Log(string message)
        {
            Debug.Log($"[E2E] t={Time.realtimeSinceStartup:0.0} {message}");
        }

        private static int ParseInt(string s) => int.TryParse(s.Trim(), out int v) ? v : 0;

        private static float ParseFloat(string s) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

        private static bool HasFlag(string[] args, string flag)
        {
            foreach (var a in args) if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static Options Parse(string[] args)
        {
            var o = new Options();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].StartsWith("-e2e", StringComparison.OrdinalIgnoreCase) && !args[i + 1].StartsWith("-")) values[args[i]] = args[i + 1];
            }
            string v;
            o.host = HasFlag(args, "-e2eHost");
            o.hostOnline = HasFlag(args, "-e2eHostOnline");
            o.switchTeam = HasFlag(args, "-e2eSwitchTeam");
            o.noDeploy = HasFlag(args, "-e2eNoDeploy");
            o.uncapFps = HasFlag(args, "-e2eUncapFps");
            if (values.TryGetValue("-e2eName", out v)) o.name = v;
            if (values.TryGetValue("-e2eJoin", out v)) o.join = v;
            if (values.TryGetValue("-e2eJoinCode", out v)) o.joinCode = v;
            if (values.TryGetValue("-e2eBrowse", out v)) o.browse = v;
            if (values.TryGetValue("-e2eMap", out v)) o.map = v;
            if (values.TryGetValue("-e2eSession", out v)) o.sessionName = v;
            if (values.TryGetValue("-e2eTickets", out v)) o.tickets = Mathf.Max(1, ParseInt(v));
            if (values.TryGetValue("-e2eTeamSize", out v)) o.teamSize = Mathf.Max(1, ParseInt(v));
            if (values.TryGetValue("-e2eBots", out v)) o.bots = v != "0";
            if (values.TryGetValue("-e2eMaxPlayers", out v)) o.maxPlayers = Mathf.Max(1, ParseInt(v));
            if (values.TryGetValue("-e2eStartWhen", out v)) o.startWhen = Mathf.Max(1, ParseInt(v));
            if (values.TryGetValue("-e2eClass", out v)) o.classIndex = ParseInt(v);
            if (values.TryGetValue("-e2eChangeClassAtPost", out v)) o.changeClassAtPost = ParseInt(v);
            if (values.TryGetValue("-e2eLeaveAfter", out v)) o.leaveAfter = ParseFloat(v);
            if (values.TryGetValue("-e2eQuitAfter", out v)) o.quitAfter = ParseFloat(v);
            if (values.TryGetValue("-e2eMatches", out v)) o.matches = Mathf.Max(1, ParseInt(v));
            if (values.TryGetValue("-e2eNetSim", out v)) o.netSim = v;
            if (values.TryGetValue("-e2eAuthProfile", out v)) o.authProfile = v;
            if (values.TryGetValue("-e2eStatsEvery", out v)) o.statsEvery = ParseFloat(v);
            if (values.TryGetValue("-e2eJoinDelay", out v)) o.joinDelay = ParseFloat(v);
            if (string.IsNullOrEmpty(o.map)) o.map = "the_frontline";
            return o;
        }
    }
}
