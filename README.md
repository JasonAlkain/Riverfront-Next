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
