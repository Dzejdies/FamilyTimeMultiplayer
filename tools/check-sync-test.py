import sys
from pathlib import Path


prefix = Path(sys.argv[1])
audio_weather = "--audio-weather" in sys.argv[2:]
shared = [
    "[WorldTest] PASS client name reaches host and echo",
    "[WorldTest] PASS water executes on host and replicates",
    "[WorldTest] PASS storm stops on both machines",
    "[Visuals] PASS hunger=0",
    "[Visuals] PASS hunger=1",
]
client_only = [
    "[WorldTest] PASS join during storm",
    "[WorldTest] PASS initial saved name",
    "[WorldTest] PASS join stops loaded weather loops",
    "[WorldTest] PASS host name reaches client",
    "[WorldTest] PASS rain and storm reach client",
    "[WorldTest] PASS client rain has visible droplets",
    "[WorldTest] PASS client rain audio is playing",
    "[WorldTest] PASS client random weather loops stopped",
    "[WorldTest] PASS lightning reaches client",
    "[WorldTest] PASS animation reaches client",
    "[WorldTest] PASS thunder leaves client commands passive",
]
failures = []
total = 0
for role in ("Host", "Client"):
    log = Path(f"{prefix}.{role.lower()}.log").read_text(encoding="utf-8", errors="replace")
    if audio_weather:
        expected = shared[:3]
        if role == "Host":
            expected += ["[AudioTest] PASS remote follow stays silent on host"]
        else:
            expected += [
                "[AudioTest] PASS host command stays on host",
                "[AudioTest] PASS client follow plays once",
                "[AudioTest] PASS client stop plays once",
                "[AudioTest] PASS client lock plays once",
                "[AudioTest] PASS client unlock plays once",
                "[AudioTest] PASS mama toggle plays once",
                "[AudioTest] PASS mama reverse toggle plays once",
            ]
            expected += [f"[AudioTest] PASS routed cue {field}" for field in (
                "commandStartAudioGroup", "commandStopAudioGroup", "dropOffStartAudioGroup", "queueAddAudioGroup",
                "queueRemoveAudioGroup", "commandConstructionAudioGroup", "commandAttackAudioGroup", "commandStationAudioGroup",
                "followStartAudioGroup", "followEndAudioGroup", "lockAudioGroup", "unlockAudioGroup",
            )]
    else:
        expected = shared + [
            f"[DespawnTest] PASS {role}: consumed material is gone",
            f"[ConstructionTest] PASS {role}: part exists and 1 consumed materials are gone",
        ]
    if role == "Client":
        expected += client_only
    for check in expected:
        if check not in log:
            failures.append(f"{role}: missing {check}")
        else:
            total += 1
    for line in log.splitlines():
        if "] FAIL " in line or "session crashed" in line or "Exception" in line:
            failures.append(f"{role}: {line}")
    print(f"{role}: {len(expected)} required checks")
if failures:
    raise SystemExit("\n".join(failures))
print(f"PASS: {total} synchronization checks, no failures or exceptions")
