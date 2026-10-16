# Rivetfront

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

  Art/, Audio/                Shared art and sound used by the game (characters, environment kits, clips)
  Settings/                   Project-wide settings: URP assets, input actions, build profiles, play mode

  Templates/                  Unity template/sample content the game builds on. Avoid editing in place.
    BuildingBlocks/           Unity Multiplayer Building Blocks
      Core/, Shooter/,        Gameplay blocks (TextMesh Pro resources live in Core/TextMesh Pro)
      Platformer/
      Common/, CommonSession/, MultiplayerSession/   Session/lobby blocks
    MultiplayerFPS/           Original Multiplayer FPS template (Netcode for Entities).
                              Its code is compiled out (needs NETCODE_ENTITIES_AVAILABLE); kept for reference.

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

A smaller server-only build comes from the **Windows Dedicated Server** build profile
(`Assets/Settings/Build Profiles`): open **File → Build Profiles**, pick it, and build. It uses the
normal scene list and player settings, so its version matches the client build. Run it the same way
(`-server` is implied). On a server the HUD, pause menu, main menu and audio switch themselves off.

Players join with **Multiplayer → Join by IP** using the address the server logs at startup.
Allow the port through the Windows firewall on the server machine. Clients and server must be the
same build (the server refuses mismatched versions).
