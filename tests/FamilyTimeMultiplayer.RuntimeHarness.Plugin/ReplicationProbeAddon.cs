using System;
using System.Buffers.Binary;
using FamilyTimeMultiplayer.Addons;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Replication;

namespace FamilyTimeMultiplayer.RuntimeHarness;

internal sealed class ReplicationProbeAddon : ISessionAddon
{
    private sealed class Counter { internal int Value; }
    private readonly string _id;
    private readonly Action<string> _log;
    private readonly Counter _counter = new();
    private IAddonReplicationContext _replication = null!;

    internal ReplicationProbeAddon(string id, Action<string> log) { _id = id; _log = log; }
    public AddonDescriptor Descriptor => new(_id, "1", "counter-v1", AddonRequirement.Optional);
    private EntityKind Kind => new(_id + ".counter");

    public void OnSessionStarted(ISessionAddonContext context)
    {
        _replication = ((IReplicatedSessionAddonContext)context).Replication;
        _replication.RegisterStateAdapter(new StateAdapter(_id, Kind, _log));
        if (_replication.IsHost) _replication.RegisterAction(new IncrementAction(_id, Kind, Increment));
        BindCounter();
        _log($"{_id}: registered native session extension, host={_replication.IsHost}");
    }

    private void BindCounter()
    {
        _replication.BindEntity(Kind, 1, _counter, 1);
        if (_replication.IsHost) _replication.PublishState(_counter, _id + ".state");
    }

    private void Increment(GenericActionContext<object> context)
    {
        ((Counter)context.Target!).Value++;
        _replication.PublishState(_counter, _id + ".state");
        _log($"{_id}: host executed once, value={_counter.Value}, player={context.AuthenticatedPlayerId}");
    }

    public void OnPeerReady(int playerId)
    {
        if (_replication.IsHost || playerId != PlayerRosterMessage.HostPlayerId) return;
        if (!_replication.TryGetIdentity(_counter, out NetworkEntityId identity, out ulong revision)) throw new InvalidOperationException("Counter binding is missing.");
        _replication.RequestAction(_id + ".increment", 1, identity, revision, Array.Empty<byte>(),
            result => _log($"{_id}: client action result={result.Outcome}"));
    }

    public void OnSessionStopped() { }
    public void OnPeerLeft(int playerId) { }
    public void OnMessage(int sender, byte[] payload) => throw new InvalidOperationException("Probe uses the generic replication endpoint.");
    public void OnWorldReloaded() => BindCounter();
    public bool TryGetAuthoritativePlayerPose(int playerId, out ClientPoseMessage pose) { pose = null!; return false; }

    private sealed class StateAdapter : IEntityComponentAdapter<object>
    {
        private readonly Action<string> _log;
        internal StateAdapter(string id, EntityKind kind, Action<string> log)
        {
            _log = log;
            Descriptor = new(id + ".state", 1, new EntityStateDomain(id + ".state"), new[] { kind });
        }
        public EntityComponentAdapterDescriptor Descriptor { get; }
        public byte[] Capture(object entity) { var bytes = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, ((Counter)entity).Value); return bytes; }
        public bool TryValidate(ReadOnlyMemory<byte> state, out string error) { error = state.Length == 4 ? "" : "Invalid counter state."; return error.Length == 0; }
        public void Apply(object entity, int schemaVersion, ReadOnlyMemory<byte> state)
        {
            ((Counter)entity).Value = BinaryPrimitives.ReadInt32LittleEndian(state.Span);
            _log($"{Descriptor.AdapterId}: client applied value={((Counter)entity).Value}");
        }
    }

    private sealed class IncrementAction : IGenericActionAdapter<object>
    {
        private readonly Action<GenericActionContext<object>> _execute;
        internal IncrementAction(string id, EntityKind kind, Action<GenericActionContext<object>> execute) { Descriptor = new(id + ".increment", 1, new[] { kind }); _execute = execute; }
        public GenericActionDescriptor Descriptor { get; }
        public bool TryValidate(ReadOnlyMemory<byte> payload, out string error) { error = payload.Length == 0 ? "" : "Counter increment carries no arguments."; return error.Length == 0; }
        public bool Authorize(GenericActionContext<object> context, ReadOnlyMemory<byte> payload, out string error) { error = context.Target is Counter ? "" : "Counter target is missing."; return error.Length == 0; }
        public void Execute(GenericActionContext<object> context, ReadOnlyMemory<byte> payload) => _execute(context);
    }
}
