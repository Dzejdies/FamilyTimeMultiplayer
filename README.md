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
| Family Time | Patreon Build 105, Windows (also through Proton) |
| Multiplayer on every peer | Family Time Multiplayer 0.2.0, network protocol 24 |
| Optional aircraft addon | Family Time Aircraft 0.1.3 on every peer; required only when using aircraft in multiplayer |
| Mod loader | BepInEx 5.4.23.5 |
| Linux / Steam Deck | Proton; set `WINEDLLOVERRIDES="winhttp=n,b" %command%` |

Every peer must use Build 105 Windows with the same game build identity and Multiplayer protocol. Linux and Steam Deck run the Windows game through Proton; the native Linux game is not the build target.

Family Time owns input, targeting, crosshair, HUD, camera, animations and local presentation. Multiplayer forwards supported shared-world mutations to the authenticated host, which checks family ownership and native gameplay rules and replicates the result. Each player retains their own family.

Local cosmetic mods do not need matching installations. Network addons declare required or optional capabilities: missing or incompatible required content rejects the connection; optional capabilities are delivered only to compatible peers. Addons can register authorized actions, entity bindings and component state through `IReplicatedSessionAddonContext`. See [the integration API](docs/mod-compatibility.md).

Unknown mods are not automatically supported. Custom code/assets must exist on the receiving peer or have an explicit adapter-defined fallback; unhandled actions cannot execute on the host. The aircraft addon remains a required network dependency when registered. Its Build 105 compatibility has not been validated.

## Identity and saves

Separate-family ownership is tied to the local key at `BepInEx/config/ftmp-player.key`. Keep it when upgrading or moving your installation. Never share it. Solo and multiplayer saves use separate collections; use the in-game import flow to import a copy into either collection.

## Build and test

See [INSTALLATION.md](INSTALLATION.md) for prerequisites, build commands, and packaging. Run Core tests with `dotnet test tests/FamilyTimeMultiplayer.Core.Tests`.

The mod is an unofficial community project and is not affiliated with the Family Time developers.
