#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
game="${FAMILYTIME_DIR:-$HOME/Games/Family Time}"
variant="${1:?Choose build105-windows}"
shift
case "$variant" in
  build105-windows) ;;
  *) echo "unknown game variant: $variant" >&2; exit 1 ;;
esac
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}" PATH="$HOME/.dotnet:$PATH"

version="$(sed -n 's/.*const string Version = \"\(.*\)\";.*/\1/p' "$root/src/FamilyTimeMultiplayer/Plugin.cs")"
protocol="$(sed -n 's/.*const int Version = \([0-9]*\);.*/\1/p' "$root/src/FamilyTimeMultiplayer.Core/Protocol/MessageCodec.cs")"
[[ -n "$version" && -n "$protocol" ]] || { echo "could not read plugin/protocol version" >&2; exit 1; }
[[ -f "$game/BepInEx/core/BepInEx.dll" ]] || { echo "BepInEx is not installed in '$game'" >&2; exit 1; }

dotnet build "$root/src/FamilyTimeMultiplayer" -c Release -nologo -v quiet -p:GameDir="$game" "$@"
build="$root/src/FamilyTimeMultiplayer/bin/Release/netstandard2.1"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
plugin_dir="$stage/BepInEx/plugins/FamilyTimeMultiplayer"
mkdir -p "$plugin_dir"
cp "$build/FamilyTimeMultiplayer.dll" "$build/FamilyTimeMultiplayer.Core.dll" "$plugin_dir/"
cat > "$stage/INSTALL.txt" <<TXT
Family Time Multiplayer $version, network protocol $protocol
Target: $variant

Requires Family Time and BepInEx 5.4.23.5 installed for this game build.
Extract the BepInEx directory into the game folder containing FamilyTime.exe.
Keep only one copy of the two Multiplayer DLLs under BepInEx/plugins; remove old root-level copies when upgrading to this subfolder layout.
Use the same Multiplayer version on every peer.
Use Build 105 Windows with the same game build identity on every peer.
Linux/Steam Deck on Proton: set WINEDLLOVERRIDES="winhttp=n,b" %command% in Steam launch options.
Keep BepInEx/config/ftmp-player.key private and preserve it when upgrading.
TXT
printf '%s\n' "Game variant: $variant" "Family Time Multiplayer: $version" "Network protocol: $protocol" > "$stage/BUILD.txt"

mkdir -p "$root/dist"
out="$root/dist/FamilyTimeMultiplayer-$version-p$protocol-$variant.zip"
rm -f "$out"
(cd "$stage" && zip -qr "$out" . -x '*.xml')
echo "$out"
unzip -l "$out" | tail -n +4 | head -n -2 | awk '{print $4}'
