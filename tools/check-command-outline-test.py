import pathlib
import sys

prefix = pathlib.Path(sys.argv[1])
required = {
    "client": {"selected", "pickup", "dropoff", "construction", "hover-selection-style", "style-replaced", "selection-survives-refresh", "despawn-clears", "async-restores", "remote-dispose-clears", "host-stays-off-client"},
    "host": {"remote-stays-off-host", "async-stays-off-host", "disposed-callback-suppressed", "host-local-preserved"},
}
for role, expected in required.items():
    lines = pathlib.Path(f"{prefix}.{role}.log").read_text(errors="replace").splitlines()
    passed = {line.split("[OutlineTest] PASS ", 1)[1].split()[0] for line in lines if "[OutlineTest] PASS " in line}
    failures = [line for line in lines if "[OutlineTest] FAIL " in line or "Exception" in line or "[Error" in line]
    missing = expected - passed
    if failures or missing:
        raise SystemExit(f"{role}: missing={sorted(missing)} failures={failures}")
    print(f"{role}: {len(expected)} outline checks passed")
