# Network play tests

How to run a networked match with more than one player on one PC (or two), automatically or by hand.

## 1. Automated runs on one PC (no hands needed)

A **development build** contains a small test driver (`Assets/Game/Conquest/Scripts/NetTest`). When a build
is started with `-e2e`, the driver plays that copy of the game by itself: it hosts or joins, picks a team
and class, readies up, deploys and redeploys after each death, and writes `[E2E]` lines to the log. It only
presses the same buttons a player can (it adds no network messages), and release builds don't contain it.

1. Close the project in Unity, then build a development client and server:
   `Tools\Build\build.cmd -Development`
2. Double-click a scenario. Each one opens several small game windows and a console window:

   | Script | What it checks |
   | --- | --- |
   | `nettest-lan.cmd` | Headless dedicated server and 3 clients by IP. Lobby, team switch, class pick, first deploy, ticket-costing respawns, change class at a post, capture/neutralise/contest, a client leaving mid-match, a late joiner, match end, results, back to the lobby |
   | `nettest-host.cmd` | A player hosts (listen server) and 2 clients join, one with 150 ms delay, 30 ms jitter and 3% packet loss. Plays 2 matches, so the second one is the rematch from the lobby |
   | `nettest-hostquit.cmd` | The host quits mid-match. Both clients must return to the main menu with a readable message |
   | `nettest-relay.cmd` | Online through Unity Relay (needs internet): host, join by code, join from the Browse list, and a wrong join code |

3. **Don't click in the game windows** while a scenario runs. A click goes to that player and makes it fire.
4. When the windows have closed by themselves, press a key in the console to print the summary. The full
   logs are in `Logs\nettest\<scenario>-<time>\`.

Things to look for in the summary:

- every client has `connected`, `lobby ...`, `ready`, `scene ConquestLargeBattlefield`, `deployed #1`
- `died` followed by `deployed #2` with the team's tickets one lower (the respawn cost)
- `post <name> owner=... contested=...` lines that match between the server and every client
- `MATCH OVER`, `results #1` (never `DUPLICATE results`), `scene MatchResults`, then `scene MainMenu` and `lobby ...`
- the host-quit clients: `disconnected`, then `scene MainMenu`
- the wrong join code: `online join FAILED: No online match uses that join code.`
- any line saying `Exception` in a log is a bug

Two things are expected on one PC and are not bugs: every copy writes the same `profile.json`, so profile
totals mix all players (and one save can fail with a "sharing violation"), and each copy needs its own
`-e2eAuthProfile` for Relay so they sign in as different players.

### Driver flags (for your own scenarios)

Start the client exe with `-e2e` plus any of these (the server takes the normal dedicated-server flags from
the main README, plus `-e2e` for its stats lines):

| Flag | Meaning |
| --- | --- |
| `-e2eHost` / `-e2eHostOnline` | Host a LAN (port 7777) or Relay match from the main menu |
| `-e2eJoin <ip:port>` / `-e2eJoinCode <code>` / `-e2eBrowse <match name>` | Join by IP, by Relay code, or from the Browse list |
| `-e2eName <name>`, `-e2eAuthProfile <name>` | Player name; separate Unity sign-in per copy |
| `-e2eMap <id>`, `-e2eTickets <n>`, `-e2eTeamSize <n>`, `-e2eBots 0/1`, `-e2eSession <name>` | Host settings (defaults: the_frontline, 30, 4, bots on) |
| `-e2eStartWhen <n>` | Host readies once the lobby has n players |
| `-e2eClass <i>`, `-e2eSwitchTeam`, `-e2eChangeClassAtPost <i>` | Lobby choices; change class at the first friendly post |
| `-e2eNoDeploy` | Stay on the deploy screen |
| `-e2eLeaveAfter <s>` | Leave the match s seconds after first deploying |
| `-e2eQuitAfter <s>`, `-e2eMatches <n>` | Quit after s seconds, or 25 s after n results |
| `-e2eNetSim <delay>,<jitter>,<loss%>` | Network Simulator on this copy's connection |
| `-e2eStatsEvery <s>`, `-e2eUncapFps` | Print `STATS`/`PERF` lines every s seconds; uncap the frame rate |

`STATS` shows Netcode payload KB/s sent and received (without UDP and transport headers), frame time,
ticks per second and round-trip time. `PERF` shows GC allocation per frame, frame cost without the
frame-rate cap's wait, and milliseconds per second spent in bot updates, bot perception and the bolt
simulation.

## 2. In the Editor with Multiplayer Play Mode

Multiplayer Play Mode is already installed (`com.unity.multiplayer.playmode`).

1. **Window → Multiplayer → Multiplayer Play Mode**, and tick **Player 2** (and Player 3 if you want).
   The first start of a virtual player takes a few minutes while it imports.
2. Open `Assets/Game/Conquest/Scenes/Bootstrap` and press Play.
3. Main Editor: **Multiplayer → Host New Game**, pick settings, then **Create Lobby & Host**.
4. Player 2 window: **Multiplayer → Join by Code / IP**, type `127.0.0.1`, join, and ready up.
5. Play the match through to the results and the lobby.

Virtual players don't get command-line flags, so the test driver doesn't run there. Use this setup for
what needs eyes and hands (below).

## 3. Two PCs on one network

1. Build `Tools\Build\build.cmd` (a development build works too) and copy `windows-client` to both PCs.
2. PC 1: **Multiplayer → Host New Game**. The lobby shows `PLAYERS JOIN AT: 192.168.x.x:7777`. When Windows asks, allow the game through the firewall on private networks.
3. PC 2: **Multiplayer → Join by Code / IP** and type that address. For Relay, host online on PC 1 and type the 6-letter code on PC 2, or pick the match in **Browse Matches**.

## 4. Hands-on checklist (needs a person)

The automated runs prove the network loop. These need someone looking at the screen, with a host and at
least one client (Multiplayer Play Mode or two PCs):

- [ ] Lobby: the host's map, tickets, team size, bots and difficulty changes show on the client; the client can't change them.
- [ ] Team switch and class pick in the lobby show on both screens.
- [ ] Deploy screen: post buttons, the respawn timer, and "no tickets" when the team is out.
- [ ] Change class at a friendly post (alive, standing in the zone) on the client.
- [ ] Capture, neutralise and contest a post with the client player; the capture bar and post icons agree on both screens.
- [ ] Kill the host's player from the client and the reverse: kill feed, hit marker, damage indicator and scoreboard (Tab) on both screens.
- [ ] Ragdolls fall the same way on both screens (blue team ragdolls by default).
- [ ] Supply droid: resupply ammo; the Engineer repairs a damaged droid with the Repair Tool.
- [ ] Grenades: throw from the client; the explosion and damage on both screens.
- [ ] Sidearms: switch (E / pad Y) on the client; others see the right weapon and its shots.
- [ ] Match end: victory/defeat banner, results screen with both players, **Return now** (host only), back to the lobby; the client's Profile page counts the match once.
- [ ] Leave from the pause menu as the client mid-match; the host keeps playing and a bot takes the slot.
- [ ] Host quits mid-match: the client sees "The host ended the match or left." on the main menu.
