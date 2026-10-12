using System;
using FamilyTimeMultiplayer.Core.Replication;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class AgentInfoStateReplicationTests
{
    [Fact]
    public void NativeInfoStateUsesBoundClientIdentityAndIgnoresDuplicateDelivery()
    {
        var identity = new NetworkEntityId(1, new EntityKind("agent"), 7);
        var hostRegistry = new GenericEntityRegistry<object>(1);
        var clientRegistry = new GenericEntityRegistry<object>(1);
        hostRegistry.Register(identity, (ushort)7, 1);
        clientRegistry.Register(identity, (ushort)21, 1);
        var host = new GenericStateReplicationService<object>(hostRegistry, true);
        var client = new GenericStateReplicationService<object>(clientRegistry, false);
        ushort appliedTo = ushort.MaxValue;
        uint info = 0;
        int nativeWrites = 0;
        var hostAdapter = new AgentInfoComponentAdapter((_, _) => throw new InvalidOperationException("Host must not apply remote state."));
        hostAdapter.SetCapturedInfo(7, 0x01020304);
        host.RegisterAdapter(hostAdapter);
        client.RegisterAdapter(new AgentInfoComponentAdapter((index, value) => { appliedTo = index; info = value; nativeWrites++; }));
        var message = host.Publish(identity, (ushort)7, AgentInfoComponentAdapter.AdapterId)!;
        Assert.Equal(new byte[] { 4, 3, 2, 1 }, message.Payload.ToArray());
        client.Apply(message);
        client.Apply(message);
        Assert.Equal((ushort)21, appliedTo);
        Assert.Equal(0x01020304u, info);
        Assert.Equal(1, nativeWrites);
    }

    [Fact]
    public void LateJoinerReceivesCurrentInfoWithoutReplayingOldVisualChanges()
    {
        var identity = new NetworkEntityId(1, new EntityKind("agent"), 7);
        var registry = new GenericEntityRegistry<object>(1);
        registry.Register(identity, (ushort)7, 1);
        var state = new GenericStateReplicationService<object>(registry, true);
        var adapter = new AgentInfoComponentAdapter((_, _) => { });
        state.RegisterAdapter(adapter);
        adapter.SetCapturedInfo(7, 1);
        state.Publish(identity, (ushort)7, AgentInfoComponentAdapter.AdapterId);
        adapter.SetCapturedInfo(7, 2);
        state.Publish(identity, (ushort)7, AgentInfoComponentAdapter.AdapterId);
        Assert.Equal(new byte[] { 2, 0, 0, 0 }, Assert.Single(state.CaptureSnapshot()).Payload.ToArray());
        Assert.False(adapter.TryValidate(new byte[3], out _));
    }
}
