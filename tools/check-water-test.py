#!/usr/bin/env python3
import pathlib
import re
import sys

prefix = pathlib.Path(sys.argv[1])
roles = {role: (prefix / f'{role}.log' if (prefix / 'host.log').is_file() else pathlib.Path(f'{prefix}.{role}.log')).read_text() for role in ('host', 'client')}
required = {'ghost-cleared', 'host-water-visible', 'host-removal-visible', 'shader-drink-visible', 'ledger-water-visible', 'ledger-reduction', 'flow-level'}
for role, log in roles.items():
    expected = required | ({'ghost-seeded', 'local-water-write-blocked', 'different-window-origin'} if role == 'client' else set())
    passed = set(re.findall(r'\[WaterTest\] PASS ([\w-]+)', log))
    failures = [line for line in log.splitlines() if '[WaterTest] FAIL' in line or '[Error' in line or 'Exception' in line or 'invalid data' in line]
    if expected - passed or failures:
        raise SystemExit(f'{role}: missing={sorted(expected - passed)} errors={failures}')
    print(f'{role}: {len(expected)} water checks passed')
for name in required:
    values = [int(re.search(rf'\[WaterTest\] PASS {name} level=(\d+)', roles[role]).group(1)) for role in ('host', 'client')]
    if abs(values[0] - values[1]) > 1:
        raise SystemExit(f'{name}: GPU levels differ host/client={values}')
print('All seven shared GPU levels match')
