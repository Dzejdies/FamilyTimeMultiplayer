#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
game="${FAMILYTIME_DIR:-$HOME/Games/Family Time}"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}" PATH="$HOME/.dotnet:$PATH"

version="$(sed -n 's/.*const string Version = "\(.*\)";.*/\1/p' "$root/src/FamilyTimeMultiplayer/Plugin.cs")"
protocol="$(sed -n 's/.*const int Version = \([0-9]*\);.*/\1/p' "$root/src/FamilyTimeMultiplayer.Core/Protocol/MessageCodec.cs")"
[ -n "$version" ] && [ -n "$protocol" ] || { echo "could not read plugin/protocol version" >&2; exit 1; }

for required in winhttp.dll doorstop_config.ini .doorstop_version BepInEx/core/BepInEx.dll; do
  [ -e "$game/$required" ] || { echo "BepInEx is not installed in '$game' (missing $required)" >&2; exit 1; }
done

dotnet build "$root/src/FamilyTimeMultiplayer" -c Release -nologo -v quiet
build="$root/src/FamilyTimeMultiplayer/bin/Release/netstandard2.1"

stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
mkdir -p "$stage/BepInEx/plugins/FamilyTimeMultiplayer" "$stage/BepInEx/patchers" "$stage/BepInEx/config"
cp "$game/winhttp.dll" "$game/doorstop_config.ini" "$game/.doorstop_version" "$stage/"
cp -r "$game/BepInEx/core" "$stage/BepInEx/"
cp "$build/FamilyTimeMultiplayer.dll" "$build/FamilyTimeMultiplayer.Core.dll" "$stage/BepInEx/plugins/FamilyTimeMultiplayer/"
cat > "$stage/INSTALL-FamilyTimeMultiplayer.txt" <<TXT
Family Time Multiplayer $version (network protocol v$protocol)

1. Unpack everything into the Family Time game folder (next to FamilyTime.exe).
   Build 91 variants are mutually compatible; all other builds must match exactly.
2. Windows: just start the game.
   Linux / Steam Deck (Proton): set the launch options to
       WINEDLLOVERRIDES="winhttp=n,b" %command%
3. In game the Multiplayer page opens on start (F8 opens it later):
   Join -> enter the host's room code (e.g. K7Q-M2X) or address (ip or ip:port, default port 27777).
   Room codes need no port forwarding: both players set Settings -> Relay server to the same relay.
   Direct addresses need TCP port 27777 reachable: same LAN, port forwarding, or a VPN (Tailscale, ZeroTier).
Both players need this exact mod version. Settings: BepInEx/config/dzejdies.familytime.multiplayer.cfg
Saved separate families use your private player key: BepInEx/config/ftmp-player.key.
Keep this file when updating or moving your own installation. Do not share it with other players.
For old saves without player identities, the host must approve the first family restore in game.
TXT

mkdir -p "$root/dist"
out="$root/dist/FamilyTimeMultiplayer-$version-p$protocol.zip"
rm -f "$out"
(cd "$stage" && zip -qr "$out" . -x '*.xml')
echo "$out"
unzip -l "$out" | tail -n +4 | head -n -2 | awk '{print $4}'
