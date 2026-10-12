# Native multiplayer redesign

Status: approved by the user. The command mutation boundary, capability negotiation, addon integration API and native appearance replication are implemented. Per-player native follow isolation is implemented. Runtime coverage and remaining limitations are recorded in [Build 105 validation](validation/2026-10-12-build105-native.md).

## Required behavior

Family Time and installed mods own local input, targeting, camera, crosshair, HUD, animation and gameplay rules. Multiplayer forwards shared-world requests, authorizes them on the host, invokes native execution and replicates authoritative results. Local presentation must continue updating while requests are pending or rejected.

Every player controls their own Wolf Mama and children. Host-side identity comes from the authenticated connection; family, range, resources and ownership are checked before mutation. Clients cannot supply authoritative world state.

## Evidence and existing work

The baseline checkpoint contained generic entity identities, component adapters, generic action messages, session endpoints and tests. Reuse these contracts rather than build a competing pipeline. Baseline documentation stated that no production gameplay component adapter was registered; generic transport alone does not provide arbitrary mod compatibility.

Before this migration, `Patches/ClientCommandPatches.cs` returned false from entire native command input callbacks on clients, including interaction cancellation and long press. `Commands/ClientCommandBridge.cs` then resolved supported targets separately and requested native crosshair cache invalidation on feedback or detach. This is evidence of a broad interception boundary, not proof of the reported crosshair bug. Runtime reproduction must establish the actual cause before claiming a fix.

Before this migration, `Core/Protocol/AddonMessages.cs` required identical registered addon sets, versions and content hashes. This is an explicit compatibility contract that must be migrated deliberately, including per-peer message routing and late-join snapshots.

## Proposed boundaries

1. Preserve native input, targeting and presentation callbacks. Intercept shared mutation entry points, with explicit action adapters for commands and tools. Cancellation and cleanup remain native. A callback that combines presentation and mutation requires an inspected, build-specific integration; simply removing its prefix would allow unauthorized local execution.
2. Use the existing generic action endpoint as the authenticated host execution path. Native game adapters invoke existing game APIs in the exception-safe remote commander context. Keep game-specific validation in its existing semantic owner, and share checks used by multiple action paths.
3. Use the existing generic registry and state service for supported additional state. Each entity/domain has one replication owner. Migrate legacy domains individually; never run legacy and generic writers for the same state simultaneously. Capture only after successful native execution, suppress echo during remote application, and retain snapshot/lifecycle/revision handling.
4. Keep host simulation authority for shared AI, time, physics and lifecycle. Local camera, hover, input and cosmetic presentation remain native. A native simulation gate is retained only where independently running the client would diverge the shared world.
5. Negotiate network capabilities rather than require identical arbitrary mod lists. Local cosmetic mods need no shared capability. Optional state can be omitted for an unsupported peer only when this preserves world consistency. Required gameplay/content dependencies must match or cause an explicit incompatibility response. Unrecognized action IDs never execute.
6. Offer explicit adapters for mod actions, component state and entity content. A mod using an observed native mutation API can reuse its native adapter. A mod bypassing those APIs needs integration. No arbitrary method invocation or automatic serialization of private object graphs.

## Different mod installations

Different local HUD, camera and cosmetic mods may coexist without network agreement. A host-only gameplay mod can replicate effects represented by shared native entities and state, provided those effects use covered integration points. A client missing a custom entity's code/assets cannot reproduce its behavior or exact appearance automatically. A fallback representation is allowed only when an adapter explicitly defines it and its gameplay contract remains coherent. Client-only gameplay actions require a host handler; unsupported actions are reported without pretending to have succeeded.

The goal is a reusable multiplayer integration layer with explicit compatibility capabilities, not a claim that every unknown mod is automatically supported.

## Folder ownership and duplication

- Core: `Actions`, `Replication`, `Compatibility`, `Families`, `Protocol`, `Transport` and existing security/persistence owners.
- Unity integration: native action adapters, native state adapters, scoped Harmony hooks and build compatibility under `Game`.
- Sessions: connection lifecycle, receive dispatch and coordination only.
- Families: player-family mapping and family-scoped native data.
- UI: multiplayer menus and remote network indicators.
- Diagnostics/runtime harness: opt-in observers and runtime validation.

Move files when the corresponding boundary is migrated. Extract repeated behavior on its second occurrence into a named function owned by that behavior's domain. Keep functions short and avoid utility dumping grounds.

## Systemic analysis

OBSERVED_FAILURE: User reports an animal interaction indicator remaining after looking away.
FAILURE_LOCATION: Native targeting/presentation during multiplayer; runtime cause unconfirmed.
CAUSAL_CONE: Reported stale presentation -> potentially omitted native callback work -> client action forwarding skips whole native input methods.
ROOT_CAUSE: Architectural coupling of input suppression and host action forwarding is confirmed; causality for this particular symptom still requires runtime evidence.
ROOT_CAUSE_OWNER: Native action integration boundary.
SIBLING_SURFACES: Follow start/stop, interact start/stop, pickup sweep, secondary long press and command locks.
LOCAL_FIX: Diagnose and restore the exact missing native cleanup if reproduced.
LOCAL_FIX_LIMITATION: Whole-callback suppression still requires each integration to preserve native lifecycle behavior manually.
SYSTEMIC_FIX: Preserve native local lifecycle and intercept authorized shared mutations through explicit adapters.
SYSTEMIC_HYPOTHESIS_EVIDENCE: Multiple production prefixes suppress whole native callbacks and duplicate target resolution in the client bridge.
REFACTOR_THRESHOLD_EVIDENCE: The same broad boundary is shared by multiple command input paths; the user explicitly requests its replacement.
FUTURE_CALLER_BEHAVIOR: Adapters use the canonical host boundary; hooks still require build-specific coverage verification.
RECOMMENDED_FIX_LEVEL: CROSS-SUBSYSTEM.
OUTCOME: CROSS_SUBSYSTEM_FIX_RECOMMENDED.
WHY: Action routing, native lifecycle and replication must agree; cosmetic resets alone cannot establish that contract.

DOMAIN_CONCEPT: Authorized native shared-world action.
SEMANTIC_OWNER: Core action endpoint/bridge, with existing game authorization owners and native game adapters.
EXISTING_EQUIVALENT: Core/Replication/GenericActionBridge.cs and existing host command/tool services.
CURRENT_CALLERS: HostSession, ClientSession, ClientCommandBridge, command/tool/hold patches.
CROSS_SUBSYSTEM: YES.
EXTRACTION_REQUIRED: YES, when migrating duplicated action validation/routing; reuse existing owners first.
WHY: Transport authenticates the peer, game policies authorize the action, native execution changes the world and replication distributes results.

## Verification and commits

First verify and commit the inherited working tree as an explicitly identified checkpoint. Record verification limits in its commit message. Keep design, capability negotiation, native actions, state adapters and folder moves as separately reviewable commits.

Use behavior tests for native local pass-through, rejection without shared mutation, successful native host execution, family isolation, duplicate requests, stale targets, missing optional capabilities, missing required content, reconnect and late join. Run Core and runtime harness contract tests and plugin compilation against available game assemblies.

In the actual game, reproduce animal hover -> look away and test interaction cancellation, long-press release, rejection, disconnect, host/client family control and differing local mods. Capture native hover/indicator state to distinguish stale targeting from dirty-cache or UI cleanup failures. Compilation and synthetic tests do not establish live-game correctness.

Do not declare the redesign complete until production action/state adapters are integrated, old competing paths are removed, coverage limits are documented and relevant runtime checks have evidence.
