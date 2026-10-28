# Riverfront Next

Multiplayer team-based Conquest shooter built with Unity 6000.6.5f1 and Netcode for GameObjects.

## Where things live

```
Assets/
  Game/Conquest/              Our game. Start here.
    Scripts/                  All gameplay code (Game.Conquest.asmdef)
      Audio, Classes, Combat, Gameplay, Lobby, Maps,
      Networking, Objectives, Player, Scenes, Settings, UI
    Data/                     Tuning assets: Classes/ (Class_*.asset), Maps/ (Map_*.asset, registry)
    Materials/                Team and character materials
    Prefabs/                  Player, bot, command post, support station, network prefab list
      UI/                    Main menu canvas, player and server cards
    Scenes/                   Bootstrap, MainMenu and the Conquest maps (these are the build scenes)

  Game/Shared/                Art and sound the game took from Unity's templates (see THIRD_PARTY_NOTICES.md)
    Audio/                    Mixer and clips (footsteps, weapons, UI and HUD)
    Characters/Armature/      Character rig, materials, animations and the two rifle animator controllers
    FX/ParticlePack/          Particle materials and textures used by the FX prefabs
    Logos/                    Project logo and splash images
    TextMesh Pro/             TMP settings, default font and shaders (Resources/ is loaded by name)
    Weapons/                  Rifle, handgun and shotgun models

  Settings/                   Project-wide settings: URP assets, input actions, build profiles, play mode

  AddressableAssetsData/, DefaultNetworkPrefabs.asset, InputSystem_Actions.inputactions
                              Left at the Assets root because Unity packages expect them there.
```

## Running a dedicated server (LAN / direct IP)

Any Windows build can run headless as a dedicated server: it has no local player, and the lobby
auto-starts once enough players have joined and are all ready.

```
"Riverfront Next.exe" -batchmode -nographics -server -map industrial_depot -port 7777 -maxPlayers 16 -name "LAN Battle"
```

| Argument | Meaning | Default |
| --- | --- | --- |
| `-server` | Run as a dedicated server (implied in a Dedicated Server build target) | off |
| `-map` | Map id from `Data/Maps` (`the_frontline`, `outpost_skirmish`, `canyon_outpost`, `industrial_depot`) | `the_frontline` |
| `-port` | UDP port to listen on (all interfaces) | `7777` |
| `-maxPlayers` | Human player cap; extra connections get "Server full" | session default |
| `-tickets`, `-teamSize`, `-bots 0/1`, `-botDifficulty Easy/Normal/Hard` | Match settings | session defaults |
| `-name` | Lobby name | session default |
| `-minPlayers` | Players needed (all ready) before the start countdown | `2` |
| `-startCountdown` | Seconds from everyone ready to the map loading | `10` |
| `-logLevel` | `verbose`, `info`, `warning`, `error` or `off`. A server stamps each line with UTC time | `info` (release), `verbose` (development) |
| `-logFile -` | Write the log to stdout instead of the player log file (Unity's own flag) | player log |

A smaller server-only build comes from the **Windows Dedicated Server** build profile
(`Assets/Settings/Build Profiles`): open **File → Build Profiles**, pick it, and build. It uses the
normal scene list and player settings, so its version matches the client build. Run it the same way
(`-server` is implied). On a server the HUD, pause menu, main menu and audio switch themselves off.

Players join with **Multiplayer → Join by IP** using the address the server logs at startup.
Allow the port through the Windows firewall on the server machine. Clients and server must be the
same build (the server refuses mismatched versions).

## Testing with several players

`Tools/NetTest/README.md` covers multi-player tests: automated host/join scenarios that run several copies
of a development build on one PC (`Tools/NetTest/nettest-*.cmd`), Multiplayer Play Mode in the Editor,
two PCs on a LAN, and a hands-on checklist.

## Building and publishing to itch.io

**From the Editor:** **Riverfront Next → Publish → Build Client and Push to itch.io** builds the client and
pushes it in one step (the upload runs in its own PowerShell window). **Push Latest Build to itch.io**
pushes the newest folder in `Builds/` without rebuilding.

**Without the Editor:** double-click `Tools/Build/build.cmd`, then `Tools/Build/publish-itch.cmd`
(close the project in Unity first; Unity locks it). From a terminal:

```
powershell -ExecutionPolicy Bypass -File Tools/Build/build.ps1 -Zip
```

This builds the Windows client and the dedicated server with the scene list and Player Settings from
the project, and writes them to `Builds/<version>/` (gitignored):

| Path | What |
| --- | --- |
| `windows-client/` | The game. This folder is what gets pushed to itch.io. |
| `windows-server/` | Dedicated server build (see above). |
| `symbols/` | Burst/IL2CPP debug folders Unity writes next to the exe. Keep for crash reports, never ship. |
| `zips/` | Zipped client and server (with `-Zip`) for sharing outside itch.io. |
| `build-info.txt` | Version, Unity version and build time. |

The version defaults to `<Player Settings version>+<git commit>` (for example `0.1.0+a418e76`), with
`-dirty` added if there are uncommitted changes. Pass `-Version 0.2.0` for a named release.
It becomes `Application.version`, which clients and servers compare when connecting, so the client
and server from one run always match. Other options: `-Targets client` or `-Targets server`,
`-Development`, `-UnityPath` (defaults to the Hub install of the version in `ProjectVersion.txt`).
The same builds are available in the Editor under **Riverfront Next → Build**. Editor builds use the active
build profile's Player Settings overrides (such as the Windows Client splash screen) and refuse to run
if that profile changes the product name.

Push the newest build to https://allyn-david.itch.io/riverfront-next:

```
powershell -ExecutionPolicy Bypass -File Tools/Build/publish-itch.ps1 -DryRun
powershell -ExecutionPolicy Bypass -File Tools/Build/publish-itch.ps1
```

It pushes `windows-client/` to the `windows` channel with `--userversion <version>`; butler only
uploads what changed. `-IncludeServer` also pushes the server to `windows-server`, and `-Version`
picks an older build. It refuses `-dirty` builds unless you pass `-AllowDirty`.

One-time setup:
1. Install [butler](https://itchio.itch.io/butler) and put it on PATH (the script also looks in `C:\butler`).
2. Run `butler login`, or set `BUTLER_API_KEY` to a key from https://itch.io/user/settings/api-keys.
   Never commit the key.
3. Create the project page on itch.io (Kind of project: Downloadable) with the URL `riverfront-next`.
   The first push creates the `windows` channel; then mark it as Windows in the page's upload list.

## Continuous integration

Every pull request and every push to `main` runs `.github/workflows/ci.yml` (GameCI):

- the EditMode tests
- a Windows client build and a Windows dedicated server build, uploaded as artifacts for 7 days

The repository needs three secrets: `UNITY_LICENSE` (a Unity Personal `.ulf`, see
[game.ci activation](https://game.ci/docs/github/activation)), `UNITY_EMAIL` and `UNITY_PASSWORD`.
Git LFS objects are cached between runs to save LFS bandwidth.

## Licence

The code is under the [MIT License](LICENSE). Third-party assets keep their own licences; see
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
