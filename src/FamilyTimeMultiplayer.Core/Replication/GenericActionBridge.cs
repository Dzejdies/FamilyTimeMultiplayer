using System;
using System.Collections.Generic;
using System.Linq;
using FamilyTimeMultiplayer.Core.Protocol;

namespace FamilyTimeMultiplayer.Core.Replication;

public sealed class GenericActionContext<TLocal>
{
    public GenericActionContext(int authenticatedPlayerId, string owner, TLocal? target)
    { AuthenticatedPlayerId = authenticatedPlayerId; Owner = owner; Target = target; }
    public int AuthenticatedPlayerId { get; }
    public string Owner { get; }
    public TLocal? Target { get; }
}

public sealed class GenericActionDescriptor
{
    private readonly HashSet<EntityKind> _targetKinds;
    public GenericActionDescriptor(string actionId, int schemaVersion, IEnumerable<EntityKind> targetKinds, bool requiresTarget = true)
    {
        if (string.IsNullOrWhiteSpace(actionId) || actionId.Length > GenericActionRequestMessage.MaxActionIdCharacters) throw new ArgumentException("Action id is invalid.", nameof(actionId));
        if (schemaVersion <= 0) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        ActionId = actionId.Trim(); SchemaVersion = schemaVersion; _targetKinds = new HashSet<EntityKind>(targetKinds ?? throw new ArgumentNullException(nameof(targetKinds)));
        if (requiresTarget && _targetKinds.Count == 0) throw new ArgumentException("Target kinds are required.", nameof(targetKinds));
        RequiresTarget = requiresTarget;
    }
    public string ActionId { get; }
    public int SchemaVersion { get; }
    public IReadOnlyCollection<EntityKind> TargetKinds => _targetKinds;
    public bool RequiresTarget { get; }
    public bool SupportsKind(EntityKind kind) => _targetKinds.Contains(kind);
}

public interface IGenericActionAdapter<TLocal>
{
    GenericActionDescriptor Descriptor { get; }
    bool TryValidate(ReadOnlyMemory<byte> payload, out string error);
    bool Authorize(GenericActionContext<TLocal> context, ReadOnlyMemory<byte> payload, out string error);
    void Execute(GenericActionContext<TLocal> context, ReadOnlyMemory<byte> payload);
}

public sealed class AuthenticatedActionPeer
{
    public AuthenticatedActionPeer(int playerId, string owner, bool isAuthenticated, bool isLive)
    { PlayerId = playerId; Owner = owner ?? throw new ArgumentNullException(nameof(owner)); IsAuthenticated = isAuthenticated; IsLive = isLive; }
    public int PlayerId { get; }
    public string Owner { get; }
    public bool IsAuthenticated { get; }
    public bool IsLive { get; }
}

public sealed class GenericActionBridge<TLocal> where TLocal : notnull
{
    private sealed class ReplayEntry
    {
        public ReplayEntry(byte[] requestFrame, GenericActionResultMessage result) { RequestFrame = requestFrame; Result = result; }
        public byte[] RequestFrame { get; }
        public GenericActionResultMessage Result { get; }
    }

    public const int MaxReplayEntries = 4096;
    private readonly GenericEntityRegistry<TLocal> _entities;
    private readonly Action<int, Action> _executionScope;
    private readonly Dictionary<string, IGenericActionAdapter<TLocal>> _adapters = new(StringComparer.Ordinal);
    private readonly Dictionary<(int Player, Guid Request), ReplayEntry> _results = new();
    private readonly Queue<(int Player, Guid Request)> _order = new();
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    public GenericActionBridge(GenericEntityRegistry<TLocal> entities, Action<int, Action>? executionScope = null)
    { _entities = entities ?? throw new ArgumentNullException(nameof(entities)); _executionScope = executionScope ?? ((_, action) => action()); }

    public void Register(IGenericActionAdapter<TLocal> adapter)
    {
        EnsureThread(); if (adapter == null) throw new ArgumentNullException(nameof(adapter));
        if (!_adapters.TryAdd(adapter.Descriptor.ActionId, adapter)) throw new InvalidOperationException($"Action '{adapter.Descriptor.ActionId}' is already registered.");
    }

    public GenericActionResultMessage Execute(int playerId, string owner, GenericActionRequestMessage request)
    {
        EnsureThread(); var key = (playerId, request.RequestId);
        byte[] requestFrame = MessageCodec.Encode(request);
        if (_results.TryGetValue(key, out ReplayEntry? prior))
            return prior.RequestFrame.SequenceEqual(requestFrame)
                ? prior.Result
                : Result(request, GenericActionOutcome.Rejected, "Request id was reused with different content.");
        GenericActionResultMessage result = ExecuteOnce(playerId, owner, request);
        _results.Add(key, new ReplayEntry(requestFrame, result)); _order.Enqueue(key);
        while (_order.Count > MaxReplayEntries) _results.Remove(_order.Dequeue());
        return result;
    }

    private GenericActionResultMessage ExecuteOnce(int playerId, string owner, GenericActionRequestMessage request)
    {
        if (request.WorldGeneration != _entities.WorldGeneration) return Result(request, GenericActionOutcome.Rejected, "World generation is stale.");
        if (!_adapters.TryGetValue(request.ActionId, out IGenericActionAdapter<TLocal>? adapter)) return Result(request, GenericActionOutcome.Rejected, "Unknown action.");
        GenericActionDescriptor descriptor = adapter.Descriptor;
        if (request.SchemaVersion != descriptor.SchemaVersion) return Result(request, GenericActionOutcome.Rejected, "Unsupported action schema.");
        TLocal? target = default;
        if (request.Target is NetworkEntityId identity)
        {
            if (!descriptor.SupportsKind(identity.Kind) || !_entities.TryGetLifecycleRevision(identity, out ulong revision) || revision != request.LifecycleRevision || !_entities.TryGetLocal(identity, out TLocal local))
                return Result(request, GenericActionOutcome.Rejected, "Target is unauthorized, missing, or stale.");
            target = local;
        }
        else if (descriptor.RequiresTarget) return Result(request, GenericActionOutcome.Rejected, "Action requires a target.");
        if (!adapter.TryValidate(request.Payload, out string validationError)) return Result(request, GenericActionOutcome.Rejected, validationError);
        var context = new GenericActionContext<TLocal>(playerId, owner, target);
        if (!adapter.Authorize(context, request.Payload, out string authorizationError)) return Result(request, GenericActionOutcome.Rejected, authorizationError);
        try { _executionScope(playerId, () => adapter.Execute(context, request.Payload)); return Result(request, GenericActionOutcome.Executed); }
        catch (Exception exception) { return Result(request, GenericActionOutcome.Failed, exception.Message); }
    }

    private static GenericActionResultMessage Result(GenericActionRequestMessage request, GenericActionOutcome outcome, string detail = "") =>
        new(request.RequestId, outcome, detail.Length <= 512 ? detail : detail.Substring(0, 512));
    private void EnsureThread() { if (Environment.CurrentManagedThreadId != _threadId) throw new InvalidOperationException("Generic actions must execute on their registration thread."); _entities.AssertAccess(); }
}

public sealed class GenericActionSessionEndpoint<TLocal> where TLocal : notnull
{
    private readonly GenericActionBridge<TLocal> _actions;
    public GenericActionSessionEndpoint(GenericActionBridge<TLocal> actions) => _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    public GenericActionResultMessage Receive(AuthenticatedActionPeer peer, GenericActionRequestMessage request)
    {
        if (peer == null) throw new ArgumentNullException(nameof(peer));
        if (!peer.IsAuthenticated || !peer.IsLive)
            return new GenericActionResultMessage(request.RequestId, GenericActionOutcome.Rejected, "An authenticated live peer is required.");
        return _actions.Execute(peer.PlayerId, peer.Owner, request);
    }
}
