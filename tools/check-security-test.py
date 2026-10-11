#!/usr/bin/env python3
import pathlib
import sys

prefix = pathlib.Path(sys.argv[1])
host = (prefix / 'host.log' if prefix.is_dir() else pathlib.Path(str(prefix) + '.host.log')).read_text()
client = (prefix / 'client.log' if prefix.is_dir() else pathlib.Path(str(prefix) + '.client.log')).read_text()
expected_host = ['pose-teleport', 'pose-origin-unchanged', 'free-item-spawn', 'free-water', 'outside-world-cell', 'batch-preflight', 'batch-left-terrain-unchanged', 'nearby-blueprint-allowed', 'command-ground-distance', 'nearby-hold-allowed', 'hold-teleport', 'hold-origin-unchanged', 'hold-release-velocity', 'legitimate-release-allowed', 'nearby-hold-again-allowed', 'released-stream-allowed', 'released-hold-teleport', 'settling-regrab-allowed', 'client-blueprint-arrived', 'shovel-tree-block-retained', 'raw-world-disconnected', 'client-native-hold-arrived', 'client-native-chop-consumed']
expected_client = ['client-blueprint-sent', 'live-after-valid-actions', 'raw-world-attack-sent', 'client-native-grabbed', 'client-native-dropped', 'client-native-chop-sent']
missing = [name for name in expected_host if f'[Security] PASS {name}' not in host] + [name for name in expected_client if f'[Security] PASS {name}' not in client]
forbidden = ['[Security] FAIL', 'Exception:', '[Error', 'native mutation failed', 'movement exceeds']
failures = [text for text in forbidden if text in host or text in client]
if missing or failures:
    raise SystemExit(f'missing={missing}; errors={failures}')
print(f'Passed {len(expected_host) + len(expected_client)} security/gameplay checks')
