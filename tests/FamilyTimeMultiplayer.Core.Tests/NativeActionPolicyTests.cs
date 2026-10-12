using System;
using FamilyTimeMultiplayer.Core.Addons;
using FamilyTimeMultiplayer.Core.Protocol;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class NativeActionPolicyTests
{
    [Fact]
    public void RestrictedActionsHonorEveryRegisteredPolicyAndPlayerIdentity()
    {
        var policies = new INativeActionPolicy[] { new Policy((_, _) => true), new Policy((id, _) => id != 7) };
        foreach (INetMessage action in RestrictedActions())
        {
            Assert.False(NativeActionPolicy.Allows(7, action, policies));
            Assert.True(NativeActionPolicy.Allows(8, action, policies));
        }
    }

    [Theory]
    [InlineData(HoldPhase.Update)]
    [InlineData(HoldPhase.End)]
    [InlineData(HoldPhase.Settle)]
    public void ExistingHoldLifecycleStillReachesItsNormalAuthorityChecks(HoldPhase phase)
    {
        var policy = new Policy((_, _) => throw new InvalidOperationException("A continuation is not a new native action."));
        Assert.True(NativeActionPolicy.Allows(7, Hold(phase), new[] { policy }));
    }

    [Fact]
    public void PickupSweepCancellationCannotBeSuppressedByAnAddonPolicy()
    {
        var policy = new Policy((_, _) => false);
        Assert.True(NativeActionPolicy.Allows(7, CommandIntentMessage.PickupSweep(-1, false), new[] { policy }));
        Assert.False(NativeActionPolicy.Allows(7, CommandIntentMessage.Follow(false), new[] { policy }));
    }

    [Fact]
    public void SessionsWithoutPoliciesRetainNativeBehavior()
    {
        foreach (INetMessage action in RestrictedActions())
            Assert.True(NativeActionPolicy.Allows(7, action, Array.Empty<INativeActionPolicy>()));
    }

    [Fact]
    public void AddonTrafficAndRawWorldEditsRemainWithTheirExistingHandlers()
    {
        var policy = new Policy((_, _) => throw new InvalidOperationException("Do not hide protocol violations or block addon controls."));
        Assert.True(NativeActionPolicy.Allows(7, new AddonMessage("test", 1, Array.Empty<byte>()), new[] { policy }));
        Assert.True(NativeActionPolicy.Allows(7, new WorldEditMessage(Array.Empty<WorldEdit>()), new[] { policy }));
    }

    private static INetMessage[] RestrictedActions() => new INetMessage[]
    {
        CommandIntentMessage.Follow(true), CommandIntentMessage.Follow(false),
        new MamaToggleMessage(1), new BedRequestMessage(true), Hold(HoldPhase.Begin),
        new ToolEditsMessage(EditTool.Axe, Array.Empty<WorldEdit>()),
        new GenericActionRequestMessage("test.action", 1, Guid.NewGuid(), 1, null, 0, Array.Empty<byte>())
    };

    private static HoldMessage Hold(HoldPhase phase) => new HoldMessage(new HoldTarget(HoldTargetKind.Agent, 1), phase, true, NetVector3.Zero, NetQuaternion.Identity, NetVector3.Zero);

    private sealed class Policy : INativeActionPolicy
    {
        private readonly Func<int, INetMessage, bool> _allows;
        public Policy(Func<int, INetMessage, bool> allows) => _allows = allows;
        public bool CanPerformNativeAction(int playerId, INetMessage message) => _allows(playerId, message);
    }
}
