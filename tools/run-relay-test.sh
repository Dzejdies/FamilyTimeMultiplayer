#!/usr/bin/env bash
set -uo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="${FTMP_TEST_OUT:-/tmp/ftmp-tests}/relay"
bep="${FAMILYTIME_DIR:-$HOME/Games/Family Time}/BepInEx"
scenario="${1:-mp-family}"
duration="${2:-60}"
relay_port="${RELAY_PORT:-27780}"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}" PATH="$HOME/.dotnet:$PATH"
mkdir -p "$out"
rm -f "$bep/LogOutput.log" "$bep/LogOutput.log.1"

relay="${RELAY:-}"
relay_pid=""
if [ -z "$relay" ]; then
  relay="127.0.0.1:$relay_port"
  dotnet run --project "$here/../src/FamilyTimeMultiplayer.Relay" -c Release -- --port "$relay_port" --bind 127.0.0.1 > "$out/relay.log" 2>&1 &
  relay_pid=$!
  sleep 8
fi
"$here/launch.sh" -ftmp-host -ftmp-name Host -ftmp-relay "$relay" -ftmp-scenario "$scenario" ${HOST_ARGS:-} > "$out/host.out" 2>&1 &
code=""
for _ in $(seq 1 120); do
  code="$(grep -oE 'relay room [A-Z0-9]{3}-[A-Z0-9]{3} is open' "$bep/LogOutput.log" 2>/dev/null | head -1 | awk '{print $3}')"
  [ -n "$code" ] && break
  sleep 2
done
[ -n "$code" ] || { echo "host never opened a relay room" >&2; [ -n "$relay_pid" ] && kill "$relay_pid"; pgrep FamilyTime.exe | xargs -r kill; exit 1; }
echo "room code $code"
sleep "${CLIENT_DELAY:-20}"
"$here/launch.sh" -ftmp-join "$code" -ftmp-relay "$relay" -ftmp-name Client -ftmp-scenario "$scenario" > "$out/client.out" 2>&1 &
for _ in $(seq 1 150); do grep -q "world loaded" "$bep/LogOutput.log.1" 2>/dev/null && break; sleep 2; done
sleep "$duration"
cp "$bep/LogOutput.log" "$out/host.log"
cp "$bep/LogOutput.log.1" "$out/client.log"
pgrep FamilyTime.exe | xargs -r kill
sleep 4
if [ -n "$relay_pid" ]; then
  kill "$relay_pid"
  pkill -x ftmp-relay || true
fi
echo "done -> $out"
