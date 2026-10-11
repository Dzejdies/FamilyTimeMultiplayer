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

Every participant must use the same Multiplayer release and compatible game build. The mod rejects incompatible builds. Build 91 variants are mutually compatible; other builds must match exactly.

## Identity and saves

Separate-family ownership is tied to the local key at `BepInEx/config/ftmp-player.key`. Keep it when upgrading or moving your installation. Never share it. Solo and multiplayer saves use separate collections; use the in-game import flow to import a copy into either collection.

## Build and test

See [INSTALLATION.md](INSTALLATION.md) for prerequisites, build commands, and packaging. Run Core tests with `dotnet test tests/FamilyTimeMultiplayer.Core.Tests`.

The mod is an unofficial community project and is not affiliated with the Family Time developers.
