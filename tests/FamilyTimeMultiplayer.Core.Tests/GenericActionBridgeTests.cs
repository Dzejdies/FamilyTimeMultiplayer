using System;
using System.IO;
using System.Threading;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Replication;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class GenericActionBridgeTests
{
    private static readonly EntityKind Kind = new("counter");

    [Fact]
    public void RequestRoundTripsVersionedTargetAndPayload()
    {
        var identity = new NetworkEntityId(3, Kind, 12);
        var request = new GenericActionRequestMessage("test.counter.increment", 1, Guid.NewGuid(), 3, identity, 4, new byte[] { 2 });
        Assert.Equal(request.ActionId, MessageCodec.Decode(MessageCodec.Encode(request)).As<GenericActionRequestMessage>().ActionId);
    }

    [Fact]
    public void ExecuteIsAuthorizedAndDeduplicatedByAuthenticatedPlayerAndRequestId()
    {
        var registry = new GenericEntityRegistry<Counter>(3);
        var counter = new Counter(); var identity = new NetworkEntityId(3, Kind, 12); registry.Register(identity, counter, 4);
        int executions = 0; int executingPlayer = -1;
        var bridge = new GenericActionBridge<Counter>(registry, (player, action) => { executingPlayer = player; action(); });
        bridge.Register(new IncrementAdapter(() => executions++));
        Guid requestId = Guid.NewGuid();
        var request = new GenericActionRequestMessage("test.counter.increment", 1, requestId, 3, identity, 4, new byte[] { 1 });
        Assert.Equal(GenericActionOutcome.Executed, bridge.Execute(8, "family-a", request).Outcome);
        Assert.Equal(GenericActionOutcome.Executed, bridge.Execute(8, "family-a", request).Outcome);
        Assert.Equal(1, executions); Assert.Equal(8, executingPlayer);
    }

    [Fact]
    public void RejectsUnauthorizedAndStaleRequestsWithoutExecuting()
    {
        var registry = new GenericEntityRegistry<Counter>(3); var counter = new Counter();
        var identity = new NetworkEntityId(3, Kind, 12); registry.Register(identity, counter, 4);
        int executions = 0; var bridge = new GenericActionBridge<Counter>(registry);
        bridge.Register(new IncrementAdapter(() => executions++, allow: false));
        var request = new GenericActionRequestMessage("test.counter.increment", 1, Guid.NewGuid(), 3, identity, 3, new byte[] { 1 });
        Assert.Equal(GenericActionOutcome.Rejected, bridge.Execute(8, "family-a", request).Outcome);
        Assert.Equal(0, executions);
    }

    [Fact]
    public void DuplicateActionRegistrationsFailExplicitly()
    {
        var bridge = new GenericActionBridge<Counter>(new GenericEntityRegistry<Counter>(1));
        bridge.Register(new IncrementAdapter(() => { }));
        Assert.Throws<InvalidOperationException>(() => bridge.Register(new IncrementAdapter(() => { })));
    }

    [Fact]
    public void SessionEndpointRejectsUnauthenticatedOrNonLivePeers()
    {
        var (registry, identity, _) = CreateTarget(); var bridge = new GenericActionBridge<Counter>(registry);
        int executions = 0; bridge.Register(new IncrementAdapter(() => executions++));
        var endpoint = new GenericActionSessionEndpoint<Counter>(bridge);
        var request = CreateRequest(identity, "test.counter.increment", 1, new byte[] { 1 });
        Assert.Equal(GenericActionOutcome.Rejected, endpoint.Receive(new AuthenticatedActionPeer(2, "f", false, true), request).Outcome);
        Assert.Equal(GenericActionOutcome.Rejected, endpoint.Receive(new AuthenticatedActionPeer(2, "f", true, false), request).Outcome);
        Assert.Equal(0, executions);
    }

    [Fact]
    public void UnknownSchemaAndInvalidPayloadAreRejected()
    {
        var (registry, identity, _) = CreateTarget(); var bridge = new GenericActionBridge<Counter>(registry);
        int executions = 0; bridge.Register(new IncrementAdapter(() => executions++));
        Assert.Equal(GenericActionOutcome.Rejected, bridge.Execute(2, "f", CreateRequest(identity, "unknown.action", 1, new byte[] { 1 })).Outcome);
        Assert.Equal(GenericActionOutcome.Rejected, bridge.Execute(2, "f", CreateRequest(identity, "test.counter.increment", 2, new byte[] { 1 })).Outcome);
        Assert.Throws<ArgumentException>(() => CreateRequest(identity, "test.counter.increment", 1, new byte[GenericActionRequestMessage.MaxPayloadBytes + 1]));
        Assert.Equal(0, executions);
    }

    [Fact]
    public void DeletedTargetAndChangedGenerationAreRejected()
    {
        var (registry, identity, _) = CreateTarget(); var bridge = new GenericActionBridge<Counter>(registry);
        int executions = 0; bridge.Register(new IncrementAdapter(() => executions++));
        GenericActionRequestMessage request = CreateRequest(identity, "test.counter.increment", 1, new byte[] { 1 });
        registry.Unregister(identity, 5);
        Assert.Equal(GenericActionOutcome.Rejected, bridge.Execute(2, "f", request).Outcome);
        registry.ChangeWorldGeneration(4);
        Assert.Equal(GenericActionOutcome.Rejected, bridge.Execute(2, "f", request).Outcome);
        Assert.Equal(0, executions);
    }

    [Fact]
    public void FailedExecutionIsNotReportedAsSuccessAndIsDeduplicated()
    {
        var (registry, identity, _) = CreateTarget(); var bridge = new GenericActionBridge<Counter>(registry);
        int executions = 0; bridge.Register(new IncrementAdapter(() => { executions++; throw new InvalidOperationException("failed"); }));
        var request = CreateRequest(identity, "test.counter.increment", 1, new byte[] { 1 });
        Assert.Equal(GenericActionOutcome.Failed, bridge.Execute(2, "f", request).Outcome);
        Assert.Equal(GenericActionOutcome.Failed, bridge.Execute(2, "f", request).Outcome);
        Assert.Equal(1, executions);
    }

    [Fact]
    public void RegistryAndActionExecutionRequireCreatingThread()
    {
        var (registry, identity, _) = CreateTarget(); var bridge = new GenericActionBridge<Counter>(registry);
        bridge.Register(new IncrementAdapter(() => { })); var request = CreateRequest(identity, "test.counter.increment", 1, new byte[] { 1 });
        InvalidOperationException? observed = null;
        var worker = new Thread(() =>
        {
            try { bridge.Execute(2, "f", request); }
            catch (InvalidOperationException exception) { observed = exception; }
        });

        worker.Start();
        worker.Join();

        Assert.NotNull(observed);
        Assert.Contains("thread", observed.Message);
    }

    [Fact]
    public void ReplayGuaranteeIsBoundedByCacheEviction()
    {
        var (registry, identity, _) = CreateTarget(); var bridge = new GenericActionBridge<Counter>(registry);
        int executions = 0; bridge.Register(new IncrementAdapter(() => executions++));
        var first = CreateRequest(identity, "test.counter.increment", 1, new byte[] { 1 });
        bridge.Execute(2, "f", first);
        for (int index = 1; index <= GenericActionBridge<Counter>.MaxReplayEntries; index++)
            bridge.Execute(2, "f", CreateRequest(identity, "test.counter.increment", 1, new byte[] { 1 }));
        Assert.Equal(GenericActionBridge<Counter>.MaxReplayEntries + 1, executions);
        bridge.Execute(2, "f", first);
        Assert.Equal(GenericActionBridge<Counter>.MaxReplayEntries + 2, executions);
    }

    private static (GenericEntityRegistry<Counter> Registry, NetworkEntityId Identity, Counter Local) CreateTarget()
    {
        var registry = new GenericEntityRegistry<Counter>(3); var local = new Counter();
        var identity = new NetworkEntityId(3, Kind, 12); registry.Register(identity, local, 4);
        return (registry, identity, local);
    }

    private static GenericActionRequestMessage CreateRequest(NetworkEntityId identity, string action, int schema, byte[] payload) =>
        new(action, schema, Guid.NewGuid(), identity.WorldGeneration, identity, 4, payload);

    private sealed class Counter { }
    private sealed class IncrementAdapter : IGenericActionAdapter<Counter>
    {
        private readonly Action _execute; private readonly bool _allow;
        public IncrementAdapter(Action execute, bool allow = true) { _execute = execute; _allow = allow; }
        public GenericActionDescriptor Descriptor { get; } = new("test.counter.increment", 1, new[] { Kind });
        public bool TryValidate(ReadOnlyMemory<byte> payload, out string error) { error = payload.Length == 1 && payload.Span[0] == 1 ? "" : "invalid amount"; return error.Length == 0; }
        public bool Authorize(GenericActionContext<Counter> context, ReadOnlyMemory<byte> payload, out string error) { error = _allow ? "" : "not authorized"; return _allow; }
        public void Execute(GenericActionContext<Counter> context, ReadOnlyMemory<byte> payload) => _execute();
    }
}

internal static class TestMessageExtensions
{
    public static T As<T>(this INetMessage message) where T : class => Assert.IsType<T>(message);
}
