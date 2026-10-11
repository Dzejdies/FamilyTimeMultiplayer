import pathlib
import sys

prefix = pathlib.Path(sys.argv[1])
required = {f"{phase}-{check}" for phase in ("client-sleeps", "host-sleeps") for check in ("need", "sleep", "refresh", "need-icon", "icons")}
for role in ("host", "client"):
    path = pathlib.Path(f"{prefix}.{role}.log")
    lines = path.read_text(errors="replace").splitlines()
    passed = {line.split("[FamilyView] PASS ", 1)[1].split()[0] for line in lines if "[FamilyView] PASS " in line}
    failures = [line for line in lines if "[FamilyView] FAIL " in line or "Exception" in line or "[Error" in line]
    missing = required - passed
    if failures or missing:
        raise SystemExit(f"{role}: missing={sorted(missing)} failures={failures}")
    print(f"{role}: {len(required)} family view checks passed")
