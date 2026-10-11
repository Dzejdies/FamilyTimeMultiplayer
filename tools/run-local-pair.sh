#!/usr/bin/env bash
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
logs="${FTMP_LOG_DIR:-/tmp/ftmp-pair}"
mkdir -p "$logs"
"$here/launch.sh" -ftmp-host -ftmp-name Host -ftmp-scenario "${HOST_SCENARIO:-walk-circle}" ${HOST_ARGS:-} >"$logs/host.out" 2>&1 &
echo "host pid $!"
sleep "${CLIENT_DELAY:-40}"
"$here/launch.sh" -ftmp-join 127.0.0.1 -ftmp-name Client -ftmp-scenario "${CLIENT_SCENARIO:-walk-circle}" >"$logs/client.out" 2>&1 &
echo "client pid $!"
wait
