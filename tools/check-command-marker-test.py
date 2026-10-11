import pathlib
import sys

prefix = pathlib.Path(sys.argv[1])
required = {
    "client": {"goto-held", "goto-moves", "goto-clears", "attack-shows", "attack-moves", "attack-toggle-clears", "attack-restarts", "attack-death-clears", "pickup-sibling", "dropoff-sibling", "construction-sibling", "siblings-clear"},
    "host": {"remote-markers-stay-off-host", "native-sibling-capture", "siblings-stay-off-host"},
}
for role, expected in required.items():
    lines = pathlib.Path(f"{prefix}.{role}.log").read_text(errors="replace").splitlines()
    passed = {line.split("[MarkerTest] PASS ", 1)[1].split()[0] for line in lines if "[MarkerTest] PASS " in line}
    failures = [line for line in lines if "[MarkerTest] FAIL " in line or "Exception" in line or "[Error" in line]
    missing = expected - passed
    if failures or missing:
        raise SystemExit(f"{role}: missing={sorted(missing)} failures={failures}")
    print(f"{role}: {len(expected)} command marker checks passed")
