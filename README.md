# Family Time Multiplayer

A host-authoritative co-op mod for Family Time. It synchronizes players, children, commands, items, construction, weather, water, family ownership and separate multiplayer saves. The host owns gameplay simulation and validates client actions.

## Install

1. Install Family Time and BepInEx 5.4.23.5 for the same game build on every computer.
2. Download the Multiplayer package matching that game build from the repository Releases page. If no release is available yet, follow [INSTALLATION.md](INSTALLATION.md) to build from source.
3. Extract the package into the game folder containing `FamilyTime.exe`, allowing files to merge.
4. On Linux or Steam Deck using Proton, set this Steam launch option:

   ```text
   WINEDLLOVERRIDES="winhttp=n,b" %command%
   ```

5. Start the game and open the Multiplayer page. One player hosts; others join using the displayed room code or a direct address.

## Compatibility

| Component | Compatible version / target |
| --- | --- |
| Family Time | Patreon Build 37 or Build 91; compile/package against the matching game assemblies |
| Multiplayer on every peer | Family Time Multiplayer 0.1.14, network protocol 20 |
| Optional aircraft addon | Family Time Aircraft 0.1.3 on every peer; required only when using aircraft in multiplayer |
| Mod loader | BepInEx 5.4.23.5 |
| Linux / Steam Deck | Proton; set `WINEDLLOVERRIDES="winhttp=n,b" %command%` |

The game checks build identity when peers connect. Build 91 variants (Linux, Windows DX11, and Windows DX12) are mutually compatible. Other supported builds must have the same game build identity on every peer. Build each plugin against the matching game's assemblies; a DLL built for one game build is not a universal binary. Windows runtime checks were performed under Proton; native Windows execution has not been verified. No other gameplay mods have been compatibility-tested; plugins that alter networking, input, save/load, or world simulation may conflict.

Every multiplayer participant must use the same Multiplayer version and protocol. The aircraft addon is optional for solo play, but multiplayer aircraft requires the same Aircraft version and matching game variant on all participants.

## Identity and saves

Separate-family ownership is tied to the local key at `BepInEx/config/ftmp-player.key`. Keep it when upgrading or moving your installation. Never share it. Solo and multiplayer saves use separate collections; use the in-game import flow to import a copy into either collection.

## Build and test

See [INSTALLATION.md](INSTALLATION.md) for prerequisites, build commands, and packaging. Run Core tests with `dotnet test tests/FamilyTimeMultiplayer.Core.Tests`.

The mod is an unofficial community project and is not affiliated with the Family Time developers.
