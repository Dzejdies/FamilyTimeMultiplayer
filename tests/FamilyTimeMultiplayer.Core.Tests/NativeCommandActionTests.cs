using System;
using FamilyTimeMultiplayer.Core.Actions;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Replication;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class NativeCommandActionTests
{
    [Fact]
    public void NativeRequestExecutesOnceUsingAuthenticatedPlayer()
    {
        int workerOrders = 0;
        int lastPlayer = -1;
        var bridge = CreateBridge((player, intent) =>
        {
            lastPlayer = player;
            if (intent.Kind == CommandIntentKind.Interact) workerOrders++;
        });
        var request = Request(CommandIntentMessage.InteractRigid(12));
        var endpoint = new GenericActionSessionEndpoint<object>(bridge);
        var peer = new AuthenticatedActionPeer(7, "family-a", true, true);
        Assert.Equal(GenericActionOutcome.Executed, endpoint.Receive(peer, request).Outcome);
        Assert.Equal(GenericActionOutcome.Executed, endpoint.Receive(peer, request).Outcome);
        Assert.Equal(1, workerOrders);
        Assert.Equal(7, lastPlayer);
    }

    [Fact]
    public void NativeAuthorizationRefusalDoesNotExecute()
    {
        int workerOrders = 0;
        var bridge = CreateBridge((_, _) => workerOrders++, (_, _) => "Target belongs to another family.");
        Assert.Equal(GenericActionOutcome.Rejected, bridge.Execute(7, "family-a", Request(CommandIntentMessage.ToggleCommandLock(12))).Outcome);
        Assert.Equal(0, workerOrders);
    }

    [Fact]
    public void OtherMessageTypesCannotBeSmuggledAsNativeCommands()
    {
        int workerOrders = 0;
        var bridge = CreateBridge((_, _) => workerOrders++);
        var request = new GenericActionRequestMessage(NativeCommandActionAdapter.ActionId, 1, Guid.NewGuid(), 1, null, 0,
            MessageCodec.Encode(new AddonMessage("unregistered", 1, Array.Empty<byte>())));
        Assert.Equal(GenericActionOutcome.Rejected, bridge.Execute(7, "family-a", request).Outcome);
        Assert.Equal(0, workerOrders);
    }

    [Fact]
    public void TruncatedPayloadIsRejectedWithoutExecuting()
    {
        int workerOrders = 0;
        var bridge = CreateBridge((_, _) => workerOrders++);
        var request = new GenericActionRequestMessage(NativeCommandActionAdapter.ActionId, 1, Guid.NewGuid(), 1, null, 0, new byte[] { (byte)MessageType.CommandIntent });
        Assert.Equal(GenericActionOutcome.Rejected, bridge.Execute(7, "family-a", request).Outcome);
        Assert.Equal(0, workerOrders);
    }

    private static GenericActionRequestMessage Request(CommandIntentMessage intent) =>
        new(NativeCommandActionAdapter.ActionId, 1, Guid.NewGuid(), 1, null, 0, MessageCodec.Encode(intent));

    private static GenericActionBridge<object> CreateBridge(Action<int, CommandIntentMessage> execute,
        Func<int, CommandIntentMessage, string?>? authorize = null)
    {
        var bridge = new GenericActionBridge<object>(new GenericEntityRegistry<object>(1));
        bridge.Register(new NativeCommandActionAdapter(execute, authorize ?? ((_, _) => null)));
        return bridge;
    }
}
