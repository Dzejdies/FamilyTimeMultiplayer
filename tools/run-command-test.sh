#!/usr/bin/env bash
set -uo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="${FTMP_TEST_OUT:-/tmp/ftmp-tests}"
bep="${FAMILYTIME_DIR:-$HOME/Games/Family Time}/BepInEx"
name="${1:-mp-commands}"
host_scenario="${2:-mp-commands}"
client_scenario="${3:-mp-commands}"
duration="${4:-150}"
mkdir -p "$out"
rm -f "$bep/LogOutput.log" "$bep/LogOutput.log.1"
HOST_ARGS="${HOST_ARGS:--ftmp-load ftmp_testbed}" HOST_SCENARIO="$host_scenario" CLIENT_SCENARIO="$client_scenario" \
  FTMP_LOG_DIR="$out/$name" "$here/run-local-pair.sh" > "$out/$name.runner" 2>&1 &
sleep 45
for _ in $(seq 1 90); do grep -q "world loaded" "$bep/LogOutput.log.1" 2>/dev/null && break; sleep 2; done
sleep "$duration"
cp "$bep/LogOutput.log" "$out/$name.host.log"
cp "$bep/LogOutput.log.1" "$out/$name.client.log"
pgrep FamilyTime.exe | xargs -r kill
sleep 6
echo "done $name -> $out"
