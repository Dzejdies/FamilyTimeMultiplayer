using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Replication;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class GenericStatePipelineTests
{
    private static readonly EntityKind TestKind = new EntityKind("test.counter");
    private static readonly EntityStateDomain TestDomain = new EntityStateDomain("test.counter");
    private static readonly ReplicationOwner Owner = new ReplicationOwner("test.counter.adapter");

    [Fact]
    public void HostStateReachesClientsAndJoinInProgressSnapshotWithoutStaleOverwrite()
    {
        var identity = new NetworkEntityId(3, TestKind, 9);
        var hostEntity = new CounterState();
        var host = CreateHost(identity, hostEntity);
        var clientAEntity = new CounterState();
        var clientBEntity = new CounterState();
        var clientA = CreateClient(identity, clientAEntity);
        var clientB = CreateClient(identity, clientBEntity);
        var networkA = new InMemoryStateNetwork();
        var networkB = new InMemoryStateNetwork();

        hostEntity.Counter = 1;
        GenericEntityStateMessage first = host.Publish(identity, hostEntity, "test.counter.adapter")!;
        hostEntity.Counter = 2;
        GenericEntityStateMessage second = host.Publish(identity, hostEntity, "test.counter.adapter")!;
        Assert.Equal((1UL, 2UL), (first.StateRevision, second.StateRevision));
        networkA.Send(first);
        networkA.Send(second);
        networkB.Send(first);
        networkB.Send(second);

        Assert.Equal(GenericStateApplyStatus.Applied, clientA.Apply(networkA.ReceiveAt(1)).Status);
        Assert.Equal(GenericStateApplyStatus.Stale, clientA.Apply(networkA.ReceiveAt(0)).Status);
        Assert.Equal(GenericStateApplyStatus.Duplicate, clientA.Apply(host.CaptureSnapshot()[0]).Status);
        Assert.Equal(GenericStateApplyStatus.Applied, clientB.Apply(networkB.ReceiveAt(0)).Status);
        networkB.Send(second);
        Assert.Equal(GenericStateApplyStatus.Applied, clientB.Apply(networkB.ReceiveAt(0)).Status);
        networkB.Send(second);
        Assert.Equal(GenericStateApplyStatus.Duplicate, clientB.Apply(networkB.ReceiveAt(0)).Status);

        var joiningEntity = new CounterState();
        GenericStateReplicationService<CounterState> joiningClient = CreateClient(identity, joiningEntity);
        var joiningNetwork = new InMemoryStateNetwork();
        foreach (GenericEntityStateMessage snapshot in host.CaptureSnapshot()) joiningNetwork.Send(snapshot);
        joiningNetwork.Send(first);
        joiningNetwork.Send(second);
        Assert.Equal(GenericStateApplyStatus.Applied, joiningClient.Apply(joiningNetwork.ReceiveAt(0)).Status);
        Assert.Equal(GenericStateApplyStatus.Stale, joiningClient.Apply(joiningNetwork.ReceiveAt(0)).Status);
        Assert.Equal(GenericStateApplyStatus.Duplicate, joiningClient.Apply(joiningNetwork.ReceiveAt(0)).Status);

        Assert.Equal((2, true), (clientAEntity.Counter, clientAEntity.Enabled));
        Assert.Equal((2, true), (clientBEntity.Counter, clientBEntity.Enabled));
        Assert.Equal((2, true), (joiningEntity.Counter, joiningEntity.Enabled));
    }

    [Fact]
    public void AuthenticatedActionSessionPublishesCounterStateToPeersAndLateJoinerExactlyOnce()
    {
        var identity = new NetworkEntityId(3, TestKind, 41);
        var hostEntity = new CounterState();
        var hostRegistry = new GenericEntityRegistry<CounterState>(3);
        hostRegistry.Register(identity, hostEntity, 1);
        GenericStateReplicationService<CounterState> hostStates = CreateService(hostRegistry, isHost: true);
        var clientAEntity = new CounterState(); var clientBEntity = new CounterState();
        GenericStateReplicationService<CounterState> clientA = CreateClient(identity, clientAEntity);
        GenericStateReplicationService<CounterState> clientB = CreateClient(identity, clientBEntity);
        var hostActions = new GenericActionBridge<CounterState>(hostRegistry);
        var publishedStates = new List<GenericEntityStateMessage>();
        hostActions.Register(new IncrementCounterAction((amount) =>
        {
            hostEntity.Counter += amount;
            publishedStates.Add(hostStates.Publish(identity, hostEntity, "test.counter.adapter")!);
        }));
        var hostSession = new InMemoryAuthenticatedHostSession(hostActions);

        var oldState = hostStates.Publish(identity, hostEntity, "test.counter.adapter")!;
        var action = new GenericActionRequestMessage("test.counter.increment", 1, Guid.NewGuid(), 3, identity, 1, new byte[] { 1 });
        GenericActionResultMessage result = hostSession.Receive(17, "family-client-a", action);
        GenericActionResultMessage conflictingDuplicate = hostSession.Receive(17, "family-client-a",
            new GenericActionRequestMessage("test.counter.increment", 1, action.RequestId, 3, identity, 1, new byte[] { 9 }));
        GenericActionResultMessage crossFamily = hostSession.Receive(18, "family-client-b",
            new GenericActionRequestMessage("test.counter.increment", 1, Guid.NewGuid(), 3, identity, 1, new byte[] { 1 }));

        Assert.Equal(GenericActionOutcome.Executed, result.Outcome);
        Assert.Equal(GenericActionOutcome.Rejected, conflictingDuplicate.Outcome);
        Assert.Equal(GenericActionOutcome.Rejected, crossFamily.Outcome);
        Assert.Equal(1, hostEntity.Counter);
        GenericEntityStateMessage newest = publishedStates.Single();
        Assert.Equal(2UL, newest.StateRevision);
        var networkA = new InMemoryStateNetwork(); networkA.Send(newest); networkA.Send(oldState);
        var networkB = new InMemoryStateNetwork(); networkB.Send(newest);
        Assert.Equal(GenericStateApplyStatus.Applied, clientA.Apply(networkA.ReceiveAt(0)).Status);
        Assert.Equal(GenericStateApplyStatus.Stale, clientA.Apply(networkA.ReceiveAt(0)).Status);
        Assert.Equal(GenericStateApplyStatus.Applied, clientB.Apply(networkB.ReceiveAt(0)).Status);

        var joiningEntity = new CounterState();
        GenericStateReplicationService<CounterState> joiningClient = CreateClient(identity, joiningEntity);
        var joiningNetwork = new InMemoryStateNetwork();
        foreach (GenericEntityStateMessage snapshot in hostStates.CaptureSnapshot()) joiningNetwork.Send(snapshot);
        Assert.Equal(GenericStateApplyStatus.Applied, joiningClient.Apply(joiningNetwork.ReceiveAt(0)).Status);
        Assert.Equal(1, clientAEntity.Counter); Assert.Equal(1, clientBEntity.Counter); Assert.Equal(1, joiningEntity.Counter);
    }

    [Fact]
    public void StateArrivingBeforeEntityBindingIsAppliedWhenEntityBecomesAvailable()
    {
        var identity = new NetworkEntityId(3, TestKind, 9);
        var hostEntity = new CounterState { Counter = 8 };
        GenericEntityStateMessage update = CreateHost(identity, hostEntity).Publish(identity, hostEntity, "test.counter.adapter")!;
        var clientRegistry = new GenericEntityRegistry<CounterState>(3);
        var client = CreateClient(clientRegistry);

        Assert.Equal(GenericStateApplyStatus.Pending, client.Apply(update).Status);
        var local = new CounterState();
        clientRegistry.Register(identity, local, 1);
        Assert.Equal(GenericStateApplyStatus.Applied, client.OnEntityAvailable(identity)[0].Status);
        Assert.Equal(8, local.Counter);
    }

    [Fact]
    public void DespawnedEntityRejectsDelayedStateAndWorldGenerationChangeInvalidatesState()
    {
        var identity = new NetworkEntityId(3, TestKind, 9);
        var hostEntity = new CounterState { Counter = 5 };
        GenericEntityStateMessage update = CreateHost(identity, hostEntity).Publish(identity, hostEntity, "test.counter.adapter")!;
        var clientRegistry = new GenericEntityRegistry<CounterState>(3);
        var local = new CounterState();
        clientRegistry.Register(identity, local, 1);
        GenericStateReplicationService<CounterState> client = CreateClient(clientRegistry);
        clientRegistry.Unregister(identity, 2);

        Assert.Equal(GenericStateApplyStatus.Rejected, client.Apply(update).Status);
        clientRegistry.ChangeWorldGeneration(4);
        Assert.Equal(GenericStateApplyStatus.Rejected, client.Apply(update).Status);
    }

    [Fact]
    public void ClientCannotPublishAuthoritativeStateAndLegacyOwnershipConflictIsRejected()
    {
        var identity = new NetworkEntityId(3, TestKind, 9);
        var local = new CounterState();
        var registry = new GenericEntityRegistry<CounterState>(3);
        registry.Register(identity, local, 1);
        registry.ClaimOwnership(identity, TestDomain, Owner);
        GenericStateReplicationService<CounterState> client = CreateClient(registry);

        Assert.Throws<InvalidOperationException>(() => client.Publish(identity, local, "test.counter.adapter"));
        Assert.Throws<InvalidOperationException>(() => registry.ClaimOwnership(
            identity, TestDomain, new ReplicationOwner("legacy.owner")));
    }

    [Fact]
    public void InvalidSyntheticPayloadIsRejectedWithoutApplying()
    {
        var identity = new NetworkEntityId(3, TestKind, 9);
        var registry = new GenericEntityRegistry<CounterState>(3);
        var local = new CounterState { Counter = 4 };
        registry.Register(identity, local, 1);
        GenericStateReplicationService<CounterState> client = CreateClient(registry);
        var invalid = new GenericEntityStateMessage(identity, 1, TestDomain, 1, 1, new byte[] { 255 });

        Assert.Equal(GenericStateApplyStatus.Rejected, client.Apply(invalid).Status);
        Assert.Equal(4, local.Counter);
    }

    [Fact]
    public void ConflictingDuplicateRevisionIsRejected()
    {
        var identity = new NetworkEntityId(3, TestKind, 9);
        var registry = new GenericEntityRegistry<CounterState>(3);
        var local = new CounterState();
        registry.Register(identity, local, 1);
        GenericStateReplicationService<CounterState> client = CreateClient(registry);
        var hostEntity = new CounterState { Counter = 1 };
        GenericEntityStateMessage first = CreateHost(identity, hostEntity)
            .Publish(identity, hostEntity, "test.counter.adapter")!;
        var conflicting = new GenericEntityStateMessage(identity, 1, TestDomain, 1, first.StateRevision,
            new CounterAdapter().Capture(new CounterState { Counter = 2 }));

        Assert.Equal(GenericStateApplyStatus.Applied, client.Apply(first).Status);
        Assert.Equal(GenericStateApplyStatus.Rejected, client.Apply(conflicting).Status);
        Assert.Equal(1, local.Counter);
    }

    [Fact]
    public void ForgettingEntityRemovesItsStateFromFutureSnapshots()
    {
        var identity = new NetworkEntityId(3, TestKind, 9);
        var entity = new CounterState { Counter = 1 };
        GenericStateReplicationService<CounterState> host = CreateHost(identity, entity);
        host.Publish(identity, entity, "test.counter.adapter");

        host.ForgetEntity(identity);

        Assert.Empty(host.CaptureSnapshot());
    }

    [Fact]
    public void DelayedStateFromPreviousEntityIncarnationIsRejected()
    {
        var identity = new NetworkEntityId(3, TestKind, 9);
        var hostEntity = new CounterState { Counter = 5 };
        GenericEntityStateMessage oldIncarnation = CreateHost(identity, hostEntity).Publish(identity, hostEntity, "test.counter.adapter")!;
        var registry = new GenericEntityRegistry<CounterState>(3);
        var firstLocal = new CounterState();
        registry.Register(identity, firstLocal, 1);
        GenericStateReplicationService<CounterState> client = CreateClient(registry);
        Assert.Equal(GenericStateApplyStatus.Applied, client.Apply(oldIncarnation).Status);
        registry.Unregister(identity, 2);
        var replacement = new CounterState { Counter = 99 };
        registry.Register(identity, replacement, 3);

        Assert.Equal(GenericStateApplyStatus.Stale, client.Apply(oldIncarnation).Status);
        Assert.Equal(99, replacement.Counter);
    }

    [Fact]
    public void PendingUpdatesHaveABoundedCapacityAndReportResynchronization()
    {
        var registry = new GenericEntityRegistry<CounterState>(3);
        GenericStateReplicationService<CounterState> client = CreateClient(registry);

        GenericStateApplyResult result = null!;
        for (ulong networkId = 1; networkId <= GenericStateReplicationService<CounterState>.MaxPendingUpdates + 1; networkId++)
        {
            var identity = new NetworkEntityId(3, TestKind, networkId);
            var message = new GenericEntityStateMessage(identity, 1, TestDomain, 1, 1, new byte[5]);
            result = client.Apply(message);
        }

        Assert.Equal(GenericStateApplyStatus.ResyncRequired, result.Status);
    }

    [Fact]
    public void AdapterCanObserveRemoteApplySuppression()
    {
        var identity = new NetworkEntityId(3, TestKind, 9);
        var hostEntity = new CounterState { Counter = 7 };
        GenericEntityStateMessage update = CreateHost(identity, hostEntity).Publish(identity, hostEntity, "test.counter.adapter")!;
        var registry = new GenericEntityRegistry<CounterState>(3);
        var local = new CounterState();
        registry.Register(identity, local, 1);
        var client = new GenericStateReplicationService<CounterState>(registry, isHost: false);
        var adapter = new CounterAdapter();
        client.RegisterAdapter(adapter);
        bool suppressionObserved = false;
        adapter.OnApply = () => suppressionObserved = client.IsApplyingRemoteState;

        Assert.Equal(GenericStateApplyStatus.Applied, client.Apply(update).Status);
        Assert.True(suppressionObserved);
        Assert.False(client.IsApplyingRemoteState);
    }

    [Fact]
    public void UnsupportedSchemaIsRejectedExplicitly()
    {
        var identity = new NetworkEntityId(3, TestKind, 9);
        var registry = new GenericEntityRegistry<CounterState>(3);
        registry.Register(identity, new CounterState(), 1);
        GenericStateReplicationService<CounterState> client = CreateClient(registry);
        var message = new GenericEntityStateMessage(identity, 1, TestDomain, 2, 1, new byte[5]);

        GenericStateApplyResult result = client.Apply(message);

        Assert.Equal(GenericStateApplyStatus.Rejected, result.Status);
        Assert.Contains("schema version", result.Error);
    }

    [Fact]
    public void UnregisteredStateDomainIsRejectedExplicitly()
    {
        var identity = new NetworkEntityId(3, TestKind, 9);
        var registry = new GenericEntityRegistry<CounterState>(3);
        registry.Register(identity, new CounterState(), 1);
        GenericStateReplicationService<CounterState> client = CreateClient(registry);
        var message = new GenericEntityStateMessage(identity, 1, new EntityStateDomain("unregistered"), 1, 1, new byte[5]);

        GenericStateApplyResult result = client.Apply(message);

        Assert.Equal(GenericStateApplyStatus.Rejected, result.Status);
        Assert.Contains("No registered adapter", result.Error);
    }

    [Fact]
    public void PipelineRespectsRegistryThreadAffinity()
    {
        var identity = new NetworkEntityId(3, TestKind, 9);
        var registry = new GenericEntityRegistry<CounterState>(3);
        GenericStateReplicationService<CounterState> client = CreateClient(registry);
        InvalidOperationException? observed = null;
        var worker = new System.Threading.Thread(() =>
        {
            try
            {
                client.OnEntityAvailable(identity);
            }
            catch (InvalidOperationException exception)
            {
                observed = exception;
            }
        });

        worker.Start();
        worker.Join();

        Assert.NotNull(observed);
        Assert.Contains("thread that created", observed.Message);
    }

    private static GenericStateReplicationService<CounterState> CreateHost(NetworkEntityId identity, CounterState local)
    {
        var registry = new GenericEntityRegistry<CounterState>(identity.WorldGeneration);
        registry.Register(identity, local, 1);
        return CreateService(registry, isHost: true);
    }

    private static GenericStateReplicationService<CounterState> CreateClient(NetworkEntityId identity, CounterState local)
    {
        var registry = new GenericEntityRegistry<CounterState>(identity.WorldGeneration);
        registry.Register(identity, local, 1);
        return CreateClient(registry);
    }

    private static GenericStateReplicationService<CounterState> CreateClient(GenericEntityRegistry<CounterState> registry) =>
        CreateService(registry, isHost: false);

    private static GenericStateReplicationService<CounterState> CreateService(
        GenericEntityRegistry<CounterState> registry,
        bool isHost)
    {
        var service = new GenericStateReplicationService<CounterState>(registry, isHost);
        service.RegisterAdapter(new CounterAdapter());
        return service;
    }

    private sealed class CounterState
    {
        public int Counter { get; set; }
        public bool Enabled { get; set; } = true;
    }

    private sealed class IncrementCounterAction : IGenericActionAdapter<CounterState>
    {
        private readonly Action<int> _increment;
        public IncrementCounterAction(Action<int> increment) => _increment = increment;
        public GenericActionDescriptor Descriptor { get; } = new("test.counter.increment", 1, new[] { TestKind });
        public bool TryValidate(ReadOnlyMemory<byte> payload, out string error)
        {
            error = payload.Length == 1 && payload.Span[0] is > 0 and <= 10 ? string.Empty : "amount must be 1..10";
            return error.Length == 0;
        }
        public bool Authorize(GenericActionContext<CounterState> context, ReadOnlyMemory<byte> payload, out string error)
        {
            error = context.Owner == "family-client-a" ? string.Empty : "wrong family";
            return error.Length == 0;
        }
        public void Execute(GenericActionContext<CounterState> context, ReadOnlyMemory<byte> payload) => _increment(payload.Span[0]);
    }

    private sealed class InMemoryAuthenticatedHostSession
    {
        private readonly GenericActionSessionEndpoint<CounterState> _endpoint;
        public InMemoryAuthenticatedHostSession(GenericActionBridge<CounterState> actions) => _endpoint = new GenericActionSessionEndpoint<CounterState>(actions);
        public GenericActionResultMessage Receive(int authenticatedPlayerId, string authenticatedFamilyOwner, GenericActionRequestMessage request)
        {
            INetMessage decodedRequest = MessageCodec.Decode(MessageCodec.Encode(request));
            byte[] responseFrame = MessageCodec.Encode(_endpoint.Receive(
                new AuthenticatedActionPeer(authenticatedPlayerId, authenticatedFamilyOwner, isAuthenticated: true, isLive: true),
                (GenericActionRequestMessage)decodedRequest));
            return Assert.IsType<GenericActionResultMessage>(MessageCodec.Decode(responseFrame));
        }
    }

    private sealed class InMemoryStateNetwork
    {
        private readonly System.Collections.Generic.List<byte[]> _frames = new System.Collections.Generic.List<byte[]>();

        public void Send(GenericEntityStateMessage message) => _frames.Add(MessageCodec.Encode(message));

        public GenericEntityStateMessage ReceiveAt(int index)
        {
            byte[] frame = _frames[index];
            _frames.RemoveAt(index);
            return Assert.IsType<GenericEntityStateMessage>(MessageCodec.Decode(frame));
        }
    }

    private sealed class CounterAdapter : IEntityComponentAdapter<CounterState>
    {
        public EntityComponentAdapterDescriptor Descriptor { get; } = new EntityComponentAdapterDescriptor(
            "test.counter.adapter", 1, TestDomain, new[] { TestKind });
        public Action? OnApply { get; set; }

        public byte[] Capture(CounterState entity)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(entity.Counter);
            writer.Write(entity.Enabled);
            return stream.ToArray();
        }

        public bool TryValidate(ReadOnlyMemory<byte> state, out string error)
        {
            error = state.Length == sizeof(int) + sizeof(bool) ? string.Empty : "Expected five bytes.";
            return error.Length == 0;
        }

        public void Apply(CounterState entity, int schemaVersion, ReadOnlyMemory<byte> state)
        {
            OnApply?.Invoke();
            using var stream = new MemoryStream(state.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);
            entity.Counter = reader.ReadInt32();
            entity.Enabled = reader.ReadBoolean();
        }
    }
}
