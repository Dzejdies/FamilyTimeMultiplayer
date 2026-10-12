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

    [Fact]
    public void TargetlessMutationCannotBypassEntityLifecycleChecks()
    {
        int workerOrders = 0;
        var bridge = CreateBridge((_, _) => workerOrders++);
        Assert.Equal(GenericActionOutcome.Rejected, bridge.Execute(7, "family-a", new GenericActionRequestMessage(NativeCommandActionAdapter.ActionId, 1, Guid.NewGuid(), 1, null, 0, MessageCodec.Encode(CommandIntentMessage.InteractRigid(12)))).Outcome);
        Assert.Equal(0, workerOrders);
    }

    [Fact]
    public void BoundTargetCanExecuteButPayloadCannotSelectAnotherEntity()
    {
        var registry = new GenericEntityRegistry<object>(1);
        var identity = new NetworkEntityId(1, new EntityKind("rigid"), 12);
        registry.Register(identity, new object(), 1);
        var bridge = new GenericActionBridge<object>(registry);
        int workerOrders = 0;
        bridge.Register(new NativeCommandActionAdapter((_, _) => workerOrders++, (_, _) => null));
        var matching = new GenericActionRequestMessage(NativeCommandActionAdapter.ActionId, 1, Guid.NewGuid(), 1, identity, 1,
            MessageCodec.Encode(CommandIntentMessage.InteractRigid(12)));
        Assert.Equal(GenericActionOutcome.Executed, bridge.Execute(7, "family-a", matching).Outcome);
        var altered = new GenericActionRequestMessage(NativeCommandActionAdapter.ActionId, 1, Guid.NewGuid(), 1, identity, 1,
            MessageCodec.Encode(CommandIntentMessage.InteractRigid(13)));
        Assert.Equal(GenericActionOutcome.Rejected, bridge.Execute(7, "family-a", altered).Outcome);
        Assert.Equal(1, workerOrders);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitCommandLockStateSurvivesWireRoundTrip(bool locked)
    {
        var intent = CommandIntentMessage.ToggleCommandLock(12);
        byte[] legacyFields = MessageCodec.Encode(intent);
        byte[] payload = new byte[legacyFields.Length + 1];
        Array.Copy(legacyFields, payload, legacyFields.Length);
        payload[1] = 7; // SetCommandLock wire kind; independent of the production enum.
        payload[payload.Length - 1] = locked ? (byte)1 : (byte)0;

        var decoded = Assert.IsType<CommandIntentMessage>(MessageCodec.Decode(payload));

        Assert.Equal(payload, MessageCodec.Encode(decoded));
        Assert.Equal(12, decoded.RigidNetId);
        Assert.Equal(CommandTargetKind.Agent, decoded.TargetKind);
    }

    [Theory]
    [InlineData(CommandIntentKind.CommandLock)]
    [InlineData(CommandIntentKind.SetCommandLock)]
    public void CommandLockCannotOmitItsVersionedAgentTarget(CommandIntentKind kind)
    {
        int mutations = 0;
        var bridge = CreateBridge((_, _) => mutations++);
        var intent = new CommandIntentMessage(kind, CommandTargetKind.None, 12, 0, 0, string.Empty, NetVector3.Zero);
        var request = new GenericActionRequestMessage(NativeCommandActionAdapter.ActionId, 1, Guid.NewGuid(), 1, null, 0,
            MessageCodec.Encode(intent));
        Assert.Equal(GenericActionOutcome.Rejected, bridge.Execute(7, "family-a", request).Outcome);
        Assert.Equal(0, mutations);
    }

    private static GenericActionRequestMessage Request(CommandIntentMessage intent) =>
        new(NativeCommandActionAdapter.ActionId, 1, Guid.NewGuid(), 1, new NetworkEntityId(1, new EntityKind(intent.TargetKind == CommandTargetKind.Agent ? "agent" : "rigid"), 12), 1, MessageCodec.Encode(intent));

    private static GenericActionBridge<object> CreateBridge(Action<int, CommandIntentMessage> execute,
        Func<int, CommandIntentMessage, string?>? authorize = null)
    {
        var registry = new GenericEntityRegistry<object>(1);
        registry.Register(new NetworkEntityId(1, new EntityKind("rigid"), 12), new object(), 1);
        registry.Register(new NetworkEntityId(1, new EntityKind("agent"), 12), new object(), 1);
        var bridge = new GenericActionBridge<object>(registry);
        bridge.Register(new NativeCommandActionAdapter(execute, authorize ?? ((_, _) => null)));
        return bridge;
    }
}
