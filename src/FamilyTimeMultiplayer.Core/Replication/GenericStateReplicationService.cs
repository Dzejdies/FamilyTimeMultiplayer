using System;
using System.Collections.Generic;
using System.Linq;
using FamilyTimeMultiplayer.Core.Protocol;

namespace FamilyTimeMultiplayer.Core.Replication;

public enum GenericStateApplyStatus
{
    Applied,
    Duplicate,
    Stale,
    Pending,
    Rejected,
    ResyncRequired,
}

public sealed class GenericStateApplyResult
{
    public GenericStateApplyResult(GenericStateApplyStatus status, string error = "")
    {
        Status = status;
        Error = error;
    }

    public GenericStateApplyStatus Status { get; }
    public string Error { get; }
}

public sealed class GenericStateReplicationService<TLocal> where TLocal : notnull
{
    public const int MaxPendingUpdates = 128;
    public const int MaxPendingPayloadBytes = 4 * 1024 * 1024;

    private readonly GenericEntityRegistry<TLocal> _entities;
    private readonly EntityComponentAdapterRegistry<TLocal> _adapters = new EntityComponentAdapterRegistry<TLocal>();
    private readonly bool _isHost;
    private readonly Dictionary<StateKey, ulong> _nextRevision = new Dictionary<StateKey, ulong>();
    private readonly Dictionary<StateKey, AppliedState> _lastApplied = new Dictionary<StateKey, AppliedState>();
    private readonly Dictionary<StateKey, GenericEntityStateMessage> _latest = new Dictionary<StateKey, GenericEntityStateMessage>();
    private readonly Dictionary<StateKey, GenericEntityStateMessage> _pending = new Dictionary<StateKey, GenericEntityStateMessage>();
    private long _observedGeneration;
    private int _pendingPayloadBytes;
    private bool _applyingRemoteState;

    private readonly struct StateKey : IEquatable<StateKey>
    {
        public StateKey(NetworkEntityId identity, EntityStateDomain domain) { Identity = identity; Domain = domain; }
        public NetworkEntityId Identity { get; }
        public EntityStateDomain Domain { get; }
        public bool Equals(StateKey other) => Identity == other.Identity && Domain == other.Domain;
        public override bool Equals(object? obj) => obj is StateKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Identity, Domain);
    }

    private readonly struct AppliedState
    {
        public AppliedState(GenericEntityStateMessage message)
        {
            Revision = message.StateRevision;
            LifecycleRevision = message.EntityLifecycleRevision;
            SchemaVersion = message.SchemaVersion;
            PayloadChecksum = Crc32.Compute(message.Payload.ToArray());
        }

        public ulong Revision { get; }
        public ulong LifecycleRevision { get; }
        public int SchemaVersion { get; }
        public uint PayloadChecksum { get; }
        public bool Matches(GenericEntityStateMessage message) =>
            LifecycleRevision == message.EntityLifecycleRevision &&
            SchemaVersion == message.SchemaVersion &&
            PayloadChecksum == Crc32.Compute(message.Payload.ToArray());
    }

    public GenericStateReplicationService(GenericEntityRegistry<TLocal> entities, bool isHost)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _isHost = isHost;
        _observedGeneration = entities.WorldGeneration;
    }

    public void ResetForSnapshot()
    {
        _entities.AssertAccess();
        ClearState();
        _observedGeneration = _entities.WorldGeneration;
    }

    public bool IsApplyingRemoteState => _applyingRemoteState;

    public void RegisterAdapter(IEntityComponentAdapter<TLocal> adapter)
    {
        EnsureGeneration();
        _adapters.Register(adapter);
    }

    public GenericEntityStateMessage? Publish(NetworkEntityId identity, TLocal local, string adapterId)
    {
        EnsureGeneration();
        if (_applyingRemoteState) return null;
        if (!_isHost) throw new InvalidOperationException("Only the host may publish authoritative component state.");
        if (!_entities.TryGetLocal(identity, out TLocal bound) || !EqualityComparer<TLocal>.Default.Equals(bound, local))
            throw new InvalidOperationException($"Entity {identity} is not bound to the supplied local object.");

        IEntityComponentAdapter<TLocal> adapter = _adapters.Get(adapterId);
        EntityComponentAdapterDescriptor descriptor = adapter.Descriptor;
        EnsureAdapterSupports(descriptor, identity.Kind, descriptor.StateDomain);
        _entities.ClaimOwnership(identity, descriptor.StateDomain, new ReplicationOwner(descriptor.AdapterId));
        if (!_entities.TryGetLifecycleRevision(identity, out ulong lifecycleRevision))
            throw new InvalidOperationException($"Entity {identity} has no lifecycle revision.");

        byte[] payload = adapter.Capture(local) ?? throw new InvalidOperationException("Component adapter returned a null payload.");
        ValidatePayload(adapter, payload);
        StateKey key = new StateKey(identity, descriptor.StateDomain);
        ulong revision = NextRevision(key);
        var message = new GenericEntityStateMessage(identity, lifecycleRevision, descriptor.StateDomain,
            descriptor.SchemaVersion, revision, payload);
        _nextRevision[key] = revision;
        _latest[key] = message;
        return message;
    }

    public IReadOnlyList<GenericEntityStateMessage> CaptureSnapshot()
    {
        EnsureGeneration();
        if (!_isHost) throw new InvalidOperationException("Only the host may capture the authoritative state snapshot.");
        return _latest.Values
            .Where(IsCurrentBinding)
            .OrderBy(message => message.Identity.Kind.Name, StringComparer.Ordinal)
            .ThenBy(message => message.Identity.NetworkId)
            .ThenBy(message => message.StateDomain.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public GenericStateApplyResult Apply(GenericEntityStateMessage message)
    {
        EnsureGeneration();
        if (_isHost) return Rejected("Host cannot apply client component state as authoritative state.");
        if (message == null) return Rejected("State message is required.");
        if (message.Identity.WorldGeneration != _observedGeneration) return Rejected("State belongs to a different world generation.");
        if (!TryGetAdapter(message, out IEntityComponentAdapter<TLocal>? adapter, out string adapterError))
            return Rejected(adapterError);
        try
        {
            if (!adapter.TryValidate(message.Payload, out string validationError))
                return Rejected(string.IsNullOrWhiteSpace(validationError) ? "Component payload is invalid." : validationError);
        }
        catch (Exception exception)
        {
            return Rejected($"Component adapter failed to validate state: {exception.Message}");
        }
        if (!_entities.TryGetLocal(message.Identity, out TLocal local)) return QueuePending(message);
        if (!_entities.TryGetLifecycleRevision(message.Identity, out ulong lifecycleRevision))
            return Rejected("Entity has no lifecycle revision.");
        if (message.EntityLifecycleRevision < lifecycleRevision) return new GenericStateApplyResult(GenericStateApplyStatus.Stale);
        if (message.EntityLifecycleRevision > lifecycleRevision) return QueuePending(message);
        if (!TryClaimOwnership(message, adapter, out string ownershipError)) return Rejected(ownershipError);
        return ApplyBound(local, adapter, message);
    }

    public IReadOnlyList<GenericStateApplyResult> OnEntityAvailable(NetworkEntityId identity)
    {
        EnsureGeneration();
        StateKey[] keys = _pending.Keys.Where(key => key.Identity == identity).ToArray();
        var results = new List<GenericStateApplyResult>(keys.Length);
        foreach (StateKey key in keys.OrderBy(key => key.Domain.Name, StringComparer.Ordinal))
        {
            GenericEntityStateMessage message = _pending[key];
            RemovePending(key, message);
            results.Add(Apply(message));
        }
        return results;
    }

    public void ForgetEntity(NetworkEntityId identity)
    {
        EnsureGeneration();
        ForgetKeys(_latest, identity);
        ForgetKeys(_pending, identity);
    }

    private GenericStateApplyResult ApplyBound(TLocal local, IEntityComponentAdapter<TLocal> adapter, GenericEntityStateMessage message)
    {
        StateKey key = new StateKey(message.Identity, message.StateDomain);
        if (_lastApplied.TryGetValue(key, out AppliedState previous))
        {
            if (message.StateRevision == previous.Revision)
                return previous.Matches(message)
                    ? new GenericStateApplyResult(GenericStateApplyStatus.Duplicate)
                    : Rejected("Conflicting component state reused an existing revision.");
            if (message.StateRevision < previous.Revision) return new GenericStateApplyResult(GenericStateApplyStatus.Stale);
        }
        try
        {
            _applyingRemoteState = true;
            adapter.Apply(local, message.SchemaVersion, message.Payload);
        }
        catch (Exception exception)
        {
            return Rejected($"Component adapter failed to apply state: {exception.Message}");
        }
        finally
        {
            _applyingRemoteState = false;
        }
        _lastApplied[key] = new AppliedState(message);
        return new GenericStateApplyResult(GenericStateApplyStatus.Applied);
    }

    private GenericStateApplyResult QueuePending(GenericEntityStateMessage message)
    {
        if (_entities.IsTombstoned(message.Identity)) return Rejected("Cannot apply state to a despawned entity.");
        var key = new StateKey(message.Identity, message.StateDomain);
        if (_pending.TryGetValue(key, out GenericEntityStateMessage? previous))
        {
            if (message.EntityLifecycleRevision == previous.EntityLifecycleRevision && message.StateRevision == previous.StateRevision)
                return PayloadMatches(message, previous)
                    ? new GenericStateApplyResult(GenericStateApplyStatus.Duplicate)
                    : Rejected("Conflicting pending component state reused an existing revision.");
            if (message.EntityLifecycleRevision < previous.EntityLifecycleRevision ||
                (message.EntityLifecycleRevision == previous.EntityLifecycleRevision && message.StateRevision < previous.StateRevision))
                return new GenericStateApplyResult(GenericStateApplyStatus.Stale);
            RemovePending(key, previous);
        }
        if (_pending.Count >= MaxPendingUpdates || _pendingPayloadBytes + message.Payload.Length > MaxPendingPayloadBytes)
            return new GenericStateApplyResult(GenericStateApplyStatus.ResyncRequired, "Pending component state capacity exceeded.");
        _pending.Add(key, message);
        _pendingPayloadBytes += message.Payload.Length;
        return new GenericStateApplyResult(GenericStateApplyStatus.Pending);
    }

    private void RemovePending(StateKey key, GenericEntityStateMessage message)
    {
        if (_pending.Remove(key)) _pendingPayloadBytes -= message.Payload.Length;
    }

    private void ForgetKeys(Dictionary<StateKey, GenericEntityStateMessage> values, NetworkEntityId identity)
    {
        StateKey[] keys = values.Keys.Where(key => key.Identity == identity).ToArray();
        foreach (StateKey key in keys)
        {
            if (ReferenceEquals(values, _pending)) _pendingPayloadBytes -= values[key].Payload.Length;
            values.Remove(key);
        }
    }

    private static bool PayloadMatches(GenericEntityStateMessage left, GenericEntityStateMessage right) =>
        left.SchemaVersion == right.SchemaVersion &&
        left.EntityLifecycleRevision == right.EntityLifecycleRevision &&
        left.Payload.Span.SequenceEqual(right.Payload.Span);

    private bool TryGetAdapter(GenericEntityStateMessage message, out IEntityComponentAdapter<TLocal> adapter, out string error)
    {
        try
        {
            adapter = _adapters.GetFor(message.Identity.Kind, message.StateDomain);
            if (adapter.Descriptor.SchemaVersion != message.SchemaVersion)
            {
                error = $"Unsupported schema version {message.SchemaVersion} for adapter '{adapter.Descriptor.AdapterId}'.";
                return false;
            }
            error = string.Empty;
            return true;
        }
        catch (KeyNotFoundException)
        {
            adapter = null!;
            error = $"No registered adapter owns '{message.Identity.Kind}/{message.StateDomain}'.";
            return false;
        }
    }

    private bool TryClaimOwnership(GenericEntityStateMessage message, IEntityComponentAdapter<TLocal> adapter, out string error)
    {
        try
        {
            _entities.ClaimOwnership(message.Identity, message.StateDomain, new ReplicationOwner(adapter.Descriptor.AdapterId));
            error = string.Empty;
            return true;
        }
        catch (InvalidOperationException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static void EnsureAdapterSupports(EntityComponentAdapterDescriptor descriptor, EntityKind kind, EntityStateDomain domain)
    {
        if (descriptor.StateDomain != domain || !descriptor.SupportedKinds.Contains(kind))
            throw new InvalidOperationException($"Adapter '{descriptor.AdapterId}' does not support '{kind}/{domain}'.");
    }

    private static void ValidatePayload(IEntityComponentAdapter<TLocal> adapter, byte[] payload)
    {
        if (payload.Length > GenericEntityStateMessage.MaxPayloadBytes)
            throw new InvalidOperationException("Component adapter payload exceeds the state message size limit.");
        if (!adapter.TryValidate(payload, out string error))
            throw new InvalidOperationException($"Component adapter produced invalid state: {error}");
    }

    private ulong NextRevision(StateKey key)
    {
        ulong previous = _nextRevision.TryGetValue(key, out ulong revision) ? revision : 0;
        if (previous == ulong.MaxValue) throw new InvalidOperationException("Component state revision is exhausted.");
        return previous + 1;
    }

    private bool IsCurrentBinding(GenericEntityStateMessage message) =>
        _entities.TryGetLifecycleRevision(message.Identity, out ulong revision) &&
        revision == message.EntityLifecycleRevision && _entities.TryGetLocal(message.Identity, out _);

    private void EnsureGeneration()
    {
        _entities.AssertAccess();
        long generation = _entities.WorldGeneration;
        if (generation == _observedGeneration) return;
        ClearState();
        _observedGeneration = generation;
    }

    private void ClearState()
    {
        _nextRevision.Clear();
        _lastApplied.Clear();
        _latest.Clear();
        _pending.Clear();
        _pendingPayloadBytes = 0;
    }

    private static GenericStateApplyResult Rejected(string error) =>
        new GenericStateApplyResult(GenericStateApplyStatus.Rejected, error);
}
