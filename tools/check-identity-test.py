#!/usr/bin/env python3
import pathlib
import re
import sys

prefix = pathlib.Path(sys.argv[1])
expected = {
    'host': {'legacy-waits-for-host', 'host-approved-binding', 'stale-restore-request-rejected', 'forged-owner-proof-rejected', 'renamed-rejoin-keeps-family', 'saved-binding-restored'},
    'client': {'authenticated-join-live', 'authenticated-rejoin-live'},
}
for role, required in expected.items():
    log = pathlib.Path(f'{prefix}.{role}.log').read_text()
    passed = set(re.findall(r'\[IdentityTest\] PASS ([\w-]+)', log))
    errors = [line for line in log.splitlines() if '[IdentityTest] FAIL' in line or '[Error' in line or 'Exception' in line]
    if required - passed or errors:
        raise SystemExit(f'{role}: missing={sorted(required - passed)} errors={errors}')
    print(f'{role}: {len(required)} identity checks passed')
