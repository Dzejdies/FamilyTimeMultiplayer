using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Replication;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class GenericBridgeRuntimeScenario : ScenarioRunner
{
    private const string AdapterId = "diagnostic.counter";
    private const string ActionId = "diagnostic.counter.increment";
    private const int SchemaVersion = 1;
    private static readonly EntityStateDomain StateDomain = new EntityStateDomain("diagnostic.counter");

    private readonly ManualLogSource _log;
    private readonly CounterAdapter _state = new CounterAdapter();
    private bool _hostRegistered;
    private bool _clientRegistered;
    private bool _actionRequested;

    public GenericBridgeRuntimeScenario(ManualLogSource log) => _log = log;

    public override void Tick(double now, ISession? session)
    {
        if (session is HostSession host) RegisterHost(host);
        if (session is ClientSession client) RegisterClient(client);
        if (session is ClientSession liveClient && liveClient.IsLive) RequestIncrementWhenReady(liveClient);
    }

    private void RegisterHost(HostSession host)
    {
        if (_hostRegistered) return;
        ushort hostAvatar = LocalPlayer.PossessedHandle.stableIndex;
        var identity = new NetworkEntityId(1, LegacyAgentIdentityAdapter.Kind, hostAvatar);
        object localEntity = hostAvatar;
        _state.Set(hostAvatar, 0);
        host.RegisterGenericStateAdapter(_state);
        host.RegisterGenericAction(new IncrementAction(context =>
        {
            ushort localIndex = (ushort)context.Target!;
            _state.Increment(localIndex);
            if (!host.PublishGenericState(identity, context.Target!, AdapterId))
                throw new InvalidOperationException("The authoritative counter state was not published.");
            _log.LogInfo($"[GenericRuntime] host executed action once; counter={_state.Get(localIndex)}");
        }));
        if (!host.PublishGenericState(identity, localEntity, AdapterId))
            throw new InvalidOperationException("The initial authoritative counter state was not published.");
        _hostRegistered = true;
        _log.LogInfo($"[GenericRuntime] host registered entity={identity} lifecycle=1 counter=0");
    }

    private void RegisterClient(ClientSession client)
    {
        if (_clientRegistered) return;
        _state.SetLogger(message => _log.LogInfo(message));
        client.RegisterGenericStateAdapter(_state);
        client.GenericActionCompleted += result =>
            _log.LogInfo($"[GenericRuntime] action result outcome={result.Outcome} detail='{result.Detail}'");
        _clientRegistered = true;
        _log.LogInfo("[GenericRuntime] client registered the versioned state adapter");
    }

    private void RequestIncrementWhenReady(ClientSession client)
    {
        RosterEntry[] hosts = client.Roster.Where(entry => entry.PlayerId == PlayerRosterMessage.HostPlayerId).ToArray();
        if (hosts.Length == 0) return;
        RosterEntry host = hosts[0];
        if (!client.Identities.TryGetLocal(host.AvatarStableIndex, out ushort localIndex)) return;
        if (_state.Get(localIndex) != 0 || _actionRequested) return;

        var identity = new NetworkEntityId(1, LegacyAgentIdentityAdapter.Kind, host.AvatarStableIndex);
        client.RequestGenericAction(ActionId, SchemaVersion, identity, lifecycleRevision: 1, payload: Array.Empty<byte>());
        _actionRequested = true;
        _log.LogInfo($"[GenericRuntime] client requested increment for {identity}");
    }

    private sealed class CounterAdapter : IEntityComponentAdapter<object>
    {
        private readonly Dictionary<ushort, int> _values = new Dictionary<ushort, int>();

        public CounterAdapter() => Descriptor = new EntityComponentAdapterDescriptor(
            AdapterId, SchemaVersion, StateDomain, new[] { LegacyAgentIdentityAdapter.Kind });

        public EntityComponentAdapterDescriptor Descriptor { get; }
        public int Get(ushort localIndex) => _values.TryGetValue(localIndex, out int value) ? value : -1;
        public void Set(ushort localIndex, int value) => _values[localIndex] = value;
        public void Increment(ushort localIndex) => Set(localIndex, Get(localIndex) + 1);
        public void SetLogger(Action<string> logger) => _logState = logger;

        public byte[] Capture(object entity) => BitConverter.GetBytes(Get((ushort)entity));

        public bool TryValidate(ReadOnlyMemory<byte> state, out string error)
        {
            error = state.Length == sizeof(int) ? string.Empty : "Counter state must contain one integer.";
            return error.Length == 0;
        }

        public void Apply(object entity, int schemaVersion, ReadOnlyMemory<byte> state)
        {
            if (schemaVersion != SchemaVersion) throw new InvalidOperationException("Unsupported counter schema.");
            if (!TryValidate(state, out string error)) throw new InvalidOperationException(error);
            ushort localIndex = (ushort)entity;
            int value = BitConverter.ToInt32(state.Span);
            Set(localIndex, value);
            _logState?.Invoke($"[GenericRuntime] client applied counter={value} localEntity={localIndex}");
        }

        private Action<string>? _logState { get; set; }
    }

    private sealed class IncrementAction : IGenericActionAdapter<object>
    {
        private readonly Action<GenericActionContext<object>> _execute;

        public IncrementAction(Action<GenericActionContext<object>> execute)
        {
            _execute = execute;
            Descriptor = new GenericActionDescriptor(ActionId, SchemaVersion, new[] { LegacyAgentIdentityAdapter.Kind });
        }

        public GenericActionDescriptor Descriptor { get; }
        public bool TryValidate(ReadOnlyMemory<byte> payload, out string error)
        {
            error = payload.IsEmpty ? string.Empty : "Increment action does not accept a payload.";
            return error.Length == 0;
        }

        public bool Authorize(GenericActionContext<object> context, ReadOnlyMemory<byte> payload, out string error)
        {
            bool authorized = context.AuthenticatedPlayerId > PlayerRosterMessage.HostPlayerId &&
                !string.IsNullOrWhiteSpace(context.Owner) && context.Target is ushort;
            error = authorized ? string.Empty : "A live family owner and registered agent target are required.";
            return authorized;
        }

        public void Execute(GenericActionContext<object> context, ReadOnlyMemory<byte> payload) => _execute(context);
    }
}
