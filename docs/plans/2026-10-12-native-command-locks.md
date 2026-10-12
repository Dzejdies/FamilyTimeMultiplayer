# Native command lock action/state continuation

Continuation of the approved native multiplayer design. User reaffirmed the architecture diagram and per-player ownership.

DOMAIN_CONCEPT: Authoritative native child command-lock state.
SEMANTIC_OWNER: Existing native command action adapter / host command authorization; dedicated command-lock component adapter owns its replicated state.
EXISTING_EQUIVALENT: Native SetCommandLocked; NativeCommandActionAdapter; generic entity/state services.
CURRENT_CALLERS: Native individual lock selection, native LockGroups bulk selection, host remote commander.
CROSS_SUBSYSTEM: YES.
EXTRACTION_REQUIRED: YES, canonical lock-state payload validation and bound native agent index.
WHY: Requested state must survive forwarding; native ledger must receive authoritative state for local UI and future native inputs.

OBSERVED_FAILURE: Static source shows SetCommandLocked(handle, desired) forwarded as ToggleCommandLock, and bulk SetCommandLocked(handles, desired) bypasses the client boundary.
FAILURE_LOCATION: Native command mutation forwarding.
CAUSAL_CONE: Desired-state native API -> toggle-only network intent / uncovered batch -> inverted or unauthorized local ledger state.
ROOT_CAUSE: Incomplete action/state contract at the mutation boundary.
ROOT_CAUSE_OWNER: Native command action integration and native ledger replication.
SIBLING_SURFACES: Individual click, continuous selection, group lock selection, host family targeting, late join.
ADDITIONAL_EVIDENCE: Native group selection loops WorkerTemplateAgentCount physical indices. The client override substitutes family count, hiding later-indexed client children; remove that structural override and filter lock candidates by family instead.
LOCAL_FIX: Preserve requested bool in a native command intent.
LOCAL_FIX_LIMITATION: Batch commands and native client ledger remain unsynchronized.
SYSTEMIC_FIX: Cover individual/batch mutations, reuse host ownership/range checks, replicate a separate ledger component through the existing entity/state service.
SYSTEMIC_HYPOTHESIS_EVIDENCE: Build105 has separate ledger IsLocked; batch writes ledger directly rather than calling single setter; current code patches only single setter and emits a toggle.
REFACTOR_THRESHOLD_EVIDENCE: Multiple native entry paths share this state; safe behavior cannot depend on each caller rebuilding forwarding.
FUTURE_CALLER_BEHAVIOR: Native single/batch lock setters use the same boundary.
RECOMMENDED_FIX_LEVEL: CROSS-SUBSYSTEM.
OUTCOME: CROSS_SUBSYSTEM_FIX_RECOMMENDED.
WHY: Action semantics, family authority and client native ledger must agree.

1. Observe failing explicit-state wire tests; add codec/native-action/state tests including malformed values, duplicate set, ownership refusal, stale lifecycle, deferred apply and late join.
2. Add explicit SetCommandLock intent, preserving legacy toggle API. Protocol25 prevents older peers receiving an unknown state domain.
3. Cover native single/batch setter forwarding and host family filters. Execute native setters on host, with the existing authenticated commander scope.
4. Add command-lock state adapter and one host publisher, using native reads, change detection, batched state delivery and snapshot refresh. Apply through native setter in authorized replication scope.
5. Test Build105 with pre-join locks, repeated own-child set/unset, bulk selection and foreign-family refusal. Preserve native targeting/UI.
6. Run tests/build/diff checks, commit cohesive deliverables, update documentation and install/package only Windows Build105.
