# Installation and development

## Player installation

Use the release archive built for your Family Time game build. Install BepInEx 5.4.23.5 first if the archive does not include a loader. Close the game, extract the archive into the folder containing `FamilyTime.exe`, and let it merge with the existing `BepInEx` directory. Start the game after extraction.

For Linux/Steam Deck on Proton, set `WINEDLLOVERRIDES="winhttp=n,b" %command%` in Steam launch options. Windows does not need this option.

All players must use the same mod version. Build 91 game variants can connect to each other; other game builds must be identical. For room codes, all players select the same relay in the in-game settings. Direct connections require the configured TCP port to be reachable.

## Build from source

Requirements: .NET 8 SDK, BepInEx 5.4.23.5, and the managed Unity/game assemblies for the target game installation. The assemblies are proprietary game files and are not included in this repository.

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet test tests/FamilyTimeMultiplayer.Core.Tests
dotnet build src/FamilyTimeMultiplayer -c Release -p:GameDir="/path/to/Family Time"
```

The default `GameDir` is `$HOME/Games/Family Time`. Build separately against each target game's managed assemblies. For a redistribution package, first install a compatible BepInEx loader in the target game folder, then run:

```bash
FAMILYTIME_DIR="/path/to/Family Time" tools/package.sh build91-linux
```

Choose `build37-windows-dx11`, `build91-linux`, `build91-windows-dx11`, or `build91-windows-dx12` to match the target installation. The plugin-only archive requires BepInEx already installed and is written under `dist/`. Do not distribute game assemblies, personal identity keys, save files or logs.
