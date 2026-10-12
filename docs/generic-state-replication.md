# Generic component state replication

The generic pipeline sends only state exposed by an explicitly registered `IEntityComponentAdapter<TLocal>`. It does not reflect over Unity components, forward client actions, or replace existing agent, rigid, family, or world edit replication.

## Registration and authority

Host and client register adapters with their session's `RegisterGenericStateAdapter` method. An adapter declares one adapter ID, schema version, state domain, and supported entity kinds. Its `Capture` method serializes host state, `TryValidate` checks the encoded payload, and `Apply` updates the already-bound local entity. The host calls `PublishGenericState` after an authorized game change and once after binding an entity to seed its current component state for future joiners. Capture is explicit; the service does not observe arbitrary fields automatically. Clients cannot publish state; the host rejects a client-originated generic state message.

The adapter ID owns its state domain for each entity. If a legacy path or another adapter already owns that domain, registration may succeed but publishing or applying the state fails explicitly when ownership is claimed. Existing legacy domains remain assigned to their current replication code.

## Wire envelope

Protocol version is 21. Peers using earlier protocol versions are rejected by the existing strict handshake check; there is no mixed-version negotiation. Message type 43 contains:

| Field | Purpose |
| --- | --- |
| Envelope version | Currently 1; unknown values are rejected |
| World generation | Rejects state from an old loaded world |
| Entity kind and network ID | Identifies the entity within that world |
| Entity lifecycle revision | Rejects delayed state from an earlier local binding of the same identity |
| State domain | Selects exactly one registered adapter |
| Component schema version | Must exactly match the registered adapter |
| State revision | Monotonically increases per entity and domain on the host |
| Payload | Adapter-defined bytes, at most 256 KiB |

The host retains the latest published message for each entity/domain and integrations should call `ForgetGenericEntityState` when a non-legacy entity is removed. During join-in-progress, it sends the current snapshot before the per-peer event backlog. A snapshot can therefore be followed by an older buffered delta; clients compare lifecycle and state revisions and ignore that stale delivery. TCP preserves message order after the backlog flush.

If a state message arrives before its entity binding, the client keeps only the latest message per entity/domain, bounded to 128 updates and 4 MiB of payload. If the bound is exceeded, the service returns `ResyncRequired`; session integration terminates the inconsistent connection so it can reload the host world. When an entity becomes available, integrations call `NotifyGenericEntityAvailable` to apply its pending state.

Applying remote state exposes `IsApplyingRemoteState` during the adapter call. Integrations must use that signal to suppress their local change observer while `Apply` mutates state; the replication service itself never emits a message from `Apply`.

## Current scope and limits

The in-memory tests exercise a synthetic counter component across two clients, a late joiner, snapshot/delta reordering, duplicate delivery, stale lifecycle updates, and bounded pending state. Session APIs transport registered state, but no production gameplay adapter is registered in this stage. Save files remain the game's native format. Adapter-specific action forwarding, automatic component discovery, external mod compatibility, and real Unity runtime validation are not included.
