using System;
using System.Collections.Generic;
using FamilyTimeMultiplayer.Core.Actions;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Replication;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class CommandLockReplicationTests
{
    [Fact]
    public void RepeatedSetRequestsPreserveRequestedStateAndCanExplicitlyUnlock()
    {
        var entities = new GenericEntityRegistry<object>(1);
        var identity = new NetworkEntityId(1, new EntityKind("agent"), 12);
        entities.Register(identity, (ushort)12, 1);
        bool nativeLocked = false;
        var bridge = new GenericActionBridge<object>(entities);
        bridge.Register(new NativeCommandActionAdapter((_, intent) => nativeLocked = intent.CommandLocked, (_, _) => null));

        Assert.Equal(GenericActionOutcome.Executed, bridge.Execute(7, "family-a", Request(identity, true)).Outcome);
        Assert.True(nativeLocked);
        Assert.Equal(GenericActionOutcome.Executed, bridge.Execute(7, "family-a", Request(identity, true)).Outcome);
        Assert.True(nativeLocked);
        Assert.Equal(GenericActionOutcome.Executed, bridge.Execute(7, "family-a", Request(identity, false)).Outcome);
        Assert.False(nativeLocked);
    }

    [Fact]
    public void HostStateReachesBothMappedClientLedgersAndLateJoinSnapshot()
    {
        var identity = new NetworkEntityId(1, new EntityKind("agent"), 12);
        var hostEntities = new GenericEntityRegistry<object>(1);
        hostEntities.Register(identity, (ushort)12, 3);
        var host = new GenericStateReplicationService<object>(hostEntities, true);
        host.RegisterAdapter(new CommandLockComponentAdapter(_ => true, (_, _) => throw new InvalidOperationException()));
        var message = host.Publish(identity, (ushort)12, CommandLockComponentAdapter.AdapterId)!;

        foreach (ushort mappedChild in new ushort[] { 88, 144 })
        {
            var entities = new GenericEntityRegistry<object>(1);
            entities.Register(identity, mappedChild, 3);
            var nativeLocks = new Dictionary<ushort, bool>();
            var client = new GenericStateReplicationService<object>(entities, false);
            client.RegisterAdapter(new CommandLockComponentAdapter(_ => false, (child, locked) => nativeLocks[child] = locked));
            foreach (var state in host.CaptureSnapshot()) client.Apply(state);
            Assert.True(nativeLocks[mappedChild]);
            Assert.Single(nativeLocks);
            Assert.Equal(GenericStateApplyStatus.Duplicate, client.Apply(message).Status);
        }
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 2 })]
    [InlineData(new byte[] { 0, 1 })]
    public void InvalidStateCannotReachNativeLedger(byte[] payload)
    {
        int mutations = 0;
        var adapter = new CommandLockComponentAdapter(_ => false, (_, _) => mutations++);
        Assert.False(adapter.TryValidate(payload, out _));
        Assert.Throws<ArgumentException>((Action)(() => adapter.Apply((ushort)12, 1, payload)));
        Assert.Equal(0, mutations);
    }

    [Fact]
    public void DeferredLockUsesTheCurrentLocalBindingWhenTheChildAppears()
    {
        var identity = new NetworkEntityId(1, new EntityKind("agent"), 12);
        var entities = new GenericEntityRegistry<object>(1);
        var client = new GenericStateReplicationService<object>(entities, false);
        ushort appliedChild = 0;
        client.RegisterAdapter(new CommandLockComponentAdapter(_ => false, (child, _) => appliedChild = child));
        var message = new GenericEntityStateMessage(identity, 3, new EntityStateDomain("command-lock"), 1, 1, new byte[] { 1 });
        Assert.Equal(GenericStateApplyStatus.Pending, client.Apply(message).Status);
        entities.Register(identity, (ushort)88, 3);
        client.OnEntityAvailable(identity);
        Assert.Equal((ushort)88, appliedChild);
    }

    private static GenericActionRequestMessage Request(NetworkEntityId identity, bool locked) =>
        new(NativeCommandActionAdapter.ActionId, NativeCommandActionAdapter.SchemaVersion, Guid.NewGuid(), 1, identity, 1,
            MessageCodec.Encode(CommandIntentMessage.SetCommandLock(12, locked)));
}
