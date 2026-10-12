# Native Multiplayer Implementation Plan

> Execution: inline, task by task with verification and a commit after each deliverable.

**Goal:** Preserve native local gameplay lifecycle while synchronizing authorized shared mutations and supported mod state.

**Architecture:** Reuse the existing generic action endpoint, entity registry and state service. Native game adapters own method integration; existing family/command/tool services retain game authorization. Compatibility negotiates shared optional capabilities and enforces required dependencies.

**Tech Stack:** C# 12, .NET 8 tests, netstandard2.1 plugin/Core, Unity, BepInEx, Harmony.

**Spec:** `docs/native-multiplayer-redesign.md` (approved by user).

## Global constraints

- Preserve native camera, input, hover and HUD lifecycle.
- Execute shared mutations on the authenticated host; never accept client world state.
- One writer per entity/state domain; reuse legacy replication until its domain migrates.
- Different assets/code require explicit compatibility or a documented fallback.
- Extract duplicated domain behavior into short, named functions on its second occurrence.
- Commit each tested deliverable; report runtime limits honestly.

## 1. Negotiate addon capabilities

Files: `Core/Protocol/AddonMessages.cs`, `Core/Protocol/Messages.cs`, `Core/Protocol/MessageCodec.cs`, `Sessions/HostSession.cs`, `Sessions/ClientSession.cs`, `Addons/SessionAddons.cs`, `tests/Core.Tests/AddonProtocolTests.cs`.

- [x] Add failing tests: unmatched optional addon permits connection; required missing addon rejects; compatible optional intersection permits messaging; mismatched optional schema is excluded; welcome preserves host manifest; changing compatibility requirement invalidates identity proof.
- [x] Run the focused tests and observe missing behavior.
- [x] Add `AddonRequirement` (required by default for existing callers), canonical shared-capability negotiation, wire serialization and protocol bump. Store the negotiated set on both peers. Route messages and ready callbacks only to peers supporting that addon.
- [x] Run Core tests and plugin build, then commit.

## 2. Native command mutation boundary

Files: `Patches/ClientCommandPatches.cs`, native integration under `Game/Actions`, `Commands/ClientCommandBridge.cs`, `Core/Actions`, runtime harness tests.

- [x] Characterize native callbacks and controller methods from the installed game assemblies. Trace accepted input, target selection and native cancellation cleanup.
- [x] Test the generic native adapter and exception-safe state application; characterize original local callbacks in the installed native assembly. Unity callback execution is validated separately by the runtime probe.
- [x] Replace whole input-callback suppression with scoped native mutation adapters. Keep native target resolution, continuous-selection crosshair cleanup and local control gating. Unsupported shared operations return explicit feedback.
- [x] Route supported native command requests through a production generic action adapter using the existing host command authorization/execution service. Keep command feedback and state replication authoritative.
- [x] Run focused/full tests and plugin build; commit.

## 3. Mod integration API and state ownership

Files: session adapter registration, `Addons` integration API and Core replication tests.

- [x] Add failing tests for registered supported mod state across host, two clients and late join; missing required adapter; optional state exclusion; rejecting client state and stale generations.
- [x] Expose one session lifecycle API for registering action/state adapters and publishing host state. Negotiate adapter capabilities before delivering state; keep native legacy domains exclusively owned by existing replication.
- [x] Integrate at least one production native state adapter where current state lacks a canonical writer; do not create a second writer for existing agent/rigid/animation domains.
- [x] Run Core tests and plugin build; commit.

## 4. Folder ownership and duplicate code

- [x] Move generic action contracts/bridge to `Core/Actions`, capability negotiation to `Core/Compatibility`, native action hooks/adapters beside their game integration, preserving public APIs where needed.
- [x] Extract repeated serialization, action forwarding, callback lifecycle and peer capability checks into their semantic owners.
- [x] Run tests, build and `git diff --check`; commit.

## 5. Runtime evidence and documentation

Files: opt-in runtime harness, `README.md`, generic pipeline docs, redesign status.

- [x] Build the test harness and plugin, use isolated game instances/save collections for host/client checks.
- [x] Execute native hover -> look away, selection release, disconnect, family follow isolation in both orders, deferred flow targets, divergent per-player HUD counts and differing optional addons. Record unexercised manual cases separately; refusals and multi-client/late-join adapter delivery have Core coverage.
- [x] Record observed results and exact remaining coverage limits. Correct stale protocol/version documentation and describe required versus optional capabilities.
- [x] Run final tests/build/status; commit documentation and verified fixes. Leave a clean tree.

Evidence: [Windows Build 105 validation](../validation/2026-10-12-build105-native.md).
