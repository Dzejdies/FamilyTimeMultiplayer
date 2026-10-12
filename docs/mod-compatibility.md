# Mod integration and compatibility

Register an `ISessionAddon` with `SessionAddonRegistry.Register` before multiplayer starts. Its descriptor includes ID, version, content hash and `AddonRequirement`. Existing three-argument descriptors remain required. Optional means the addon can safely be absent on another peer; it does not supply missing assets or gameplay automatically.

The authenticated Hello and host Welcome exchange descriptors. Matching capabilities share addon messages, generic state and actions. Missing or mismatched required capabilities reject joining. Missing or mismatched optional capabilities are excluded for that peer. An addon must not declare optional content whose absence makes the shared world invalid.

In `OnSessionStarted`, cast the supplied context to `IReplicatedSessionAddonContext` and use its `Replication` member:

- Register component adapters on both peers with `RegisterStateAdapter`; register authorized host handlers with `RegisterAction` on the host.
- Prefix adapter IDs, state domains, custom entity kinds and action IDs with the addon ID followed by `.`. The capability registry rejects ownership outside that scope.
- Bind custom entities with a stable network ID and host lifecycle revision using `BindEntity`. The addon owns creation/assets and must deliver its supported lifecycle messages before state can apply. Call `UnbindEntity` on removal and rebind after `OnWorldReloaded`.
- Capture state on the host after native execution using `PublishState`. Clients never publish authoritative state. One adapter owns each entity/domain; native legacy domains cannot be claimed by a second writer.
- Use `TryGetIdentity` and `RequestAction` on the client, supplying the current world/lifecycle revision. Validate payload, ownership, range and resources in the host adapter before calling native mechanics.
- Check `IsApplyingRemoteState` in change observers to avoid echo. The Unity session also authorizes native mutation hooks during state application; it restores that scope on exceptions and deferred delivery.

Callbacks stop accepting requests when the context is released. Dispose addon-owned resources in `OnSessionStopped`. A result of `Failed` may represent partial native execution; neither failure nor disconnect permits automatic retry of a non-idempotent action. The generic bridge deduplicates only within its bounded session cache.

The native command adapter forwards supported follow, attack, pickup, dropoff, station and construction intents using targets chosen by the game. Family authorization stays in the existing host command service. New native mechanics and mods that mutate outside those entry points need additional inspected adapters. Worker acquisition and shared simulation remain gated on clients to prevent independent world mutation.

Custom models, components, animations or arbitrary mod behavior are not synthesized on peers missing their implementation. Provide required matching content or an explicitly designed fallback. No unrestricted remote method invocation or reflection-based object graph replication is provided.

Native command locks use explicit requested state through the existing command action adapter. Both single-agent and batch native setters are covered. The host checks the authenticated player's family and target reach, runs its native setter, and publishes the `command-lock` component through `familytime.command-lock`. Clients apply that component to their native ledger in the existing authorized replication scope. The snapshot includes current locks for late join. This is separate from native appearance state; no private object graph is serialized.

`WorkerTemplateAgentCount` retains its native physical-range meaning. Family HUD counts and lock candidates are scoped separately. A custom HUD can read `FamilyHud.Override` for the local family; it should not reinterpret the native physical-range size as the local family count. Existing seven-argument command constructors and the original host observer interface remain available.
