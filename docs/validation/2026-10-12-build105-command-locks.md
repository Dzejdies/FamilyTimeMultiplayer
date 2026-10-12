# Native command lock validation — Windows Build 105

Release: Multiplayer 0.2.1, protocol 25. Only Windows Build 105 `FamilyTime.exe` was run, using GE-Proton11-5 with active Direct3D11/compute in batch mode. The game identity remains `7ddcd8f6e25c4b7a9d21e4d54ebc8d90`; Assembly-CSharp SHA256 remains `442cf31b18f6ae32879691976b8719e34f887c4a8300eb50c99038b662b1ef27`.

## Verified contract

Native input, targeting, menu controls and rendering belong to the game. Single and batch SetCommandLocked mutations cross the existing native action boundary with their requested bool intact. Host authorization uses the authenticated player's existing family/range policy. Native host setters perform the changes; a dedicated generic component adapter mirrors the resulting ledger state. Current state is included in late-join snapshots.

Native lock candidates exclude foreign families. Setter guards independently enforce that ownership at the mutation boundary, reusing the same family predicate. Authorized client state application can mirror foreign child state for the shared world without granting local command permission.

The physical worker-template size remains native, so native loops can reach later-indexed client children. Per-player HUD counts remain supplied through FamilyHud. The seven-argument command constructor remains binary compatible; a new optional host observer interface preserves the original observer contract.

## Automated evidence

Observed red tests: new explicit-state wire kinds were rejected; the old toggle accepted a targetless request; adding an optional constructor argument removed the seven-argument ABI. Each now passes after its contract correction.

297 Core tests and 43 harness contract tests passed. Release production and harness builds passed with zero warnings/errors. Core tests cover explicit true/false requests, repeated set/unset, canonical state payloads, versioned target requirements, duplicate state, mapped client identities, deferred state and late-join state delivery. Existing family, scope, transport, action and replication tests remain green.

## Live engine evidence

Passing reports are retained locally in ignored `dist/native-runtime/reports`:

- Host: `native-locks-d4a22c1f65eb45bbac3bcde890891d00.json`.
- Client: `native-locks-2d0db6f3b3bd4896b6058143f96dccaa.json`.

| Scenario | Observed result |
| --- | --- |
| Host locks its child before client joins | Client receives locked native ledger state; generic registry claims the command-lock component owner |
| Two distinct own-child requests set true | Both execute; second request leaves the child locked |
| Explicit set false | Native client ledger becomes unlocked |
| Native batch contains three own children plus one foreign child | Exactly 3 requests admitted; all own children become locked |
| Native ReleaseAllCommandLocks | Own children unlock; foreign host child stays locked |
| Forged native action requests foreign-child unlock | Host rejects the request; foreign lock is unchanged |
| Native host setter targets client-family child | Native mutation is refused and state is unchanged |
| Native UI candidate/HasCommandLocks reads | Foreign child is excluded; own locks become visible and disappear after release |
| Structural worker count versus HUD | Native count 7; client HUD 3 children and 3 hungry, with 3 family members |

Runtime DLL SHA256: production `288b96b440d7d1e75b20a5c504c74c5fec08a136461c251c289b6b8c51b7d7ac`; Core `1a24a96a1b7ae8eaafaa719e6964e7fcb6b7e42d38e36ca2e981914b30c71d05`. Packaging rebuilds with the final Git revision in assembly metadata, so release hashes can differ.

The harness invokes real native setters/group release on Unity's main thread. It does not fabricate lock flags, native ledger counts or successful action results. An earlier additional host-only repeat timed out waiting for a client while implementation review continued; that run is not counted as a pass.

## Existing native lifecycle regression check

A separate protocol 25 `native-input` pair also passed: host `native-input-82cd51ab8c4849dab0bf069d4534a9e0.json`, client `native-input-d797f7fcc24746fc9e3eca2a8244dc1e.json`. Both follow orders retained separate child sets and avatar targets. Native animal hover was acquired and cleared after looking upward; the interaction indicator cleared; continuous-selection crosshair and attack selection released; disconnect cleared the crosshair and accepted follow input; completion-time retry remained rejected. Native per-family HUD fields also passed.

## Coverage limits

The live fixture covers individual locks, the batch setter and native release-all. It does not claim a complete manual matrix for every category button, bowls/fishing mechanics, every mod combination or large families near transport limits. Existing generic rejection and lifecycle protections remain active. Mechanics outside covered native integration points still require explicit adapters.

DOMAIN_RULE_DUPLICATION_ADDED = NO
GENERIC_HELPER_DUMPING_GROUND_ADDED = NO
PREMATURE_ABSTRACTION_ADDED = NO
CANONICAL_OWNER_IDENTIFIED = YES
EXISTING_CANONICAL_LOGIC_REUSED = YES

SYSTEMIC_REFACTOR_VALUE
CURRENT_BUG_FIXED = YES
ROOT_CAUSE_REMOVED = YES
SIBLINGS_PROTECTED = Single and batch setters, individual selection, native release-all, host/client family permissions, late join.
FUTURE_CALLER_SAFE_BY_DEFAULT = YES, through the covered native setters and generic state path.
INVALID_USE_HARDER_OR_IMPOSSIBLE = YES, requested state and versioned agent targets are explicit; foreign-family mutations are refused.
