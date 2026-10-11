using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Safety;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class AuthorityTests
{
    [Fact]
    public void ToolRequestHasAnExplicitAuthorityScope()
    {
        byte[] frame = { 35, 0, 0,0,0,0 };
        Assert.Equal(frame, MessageCodec.Encode(MessageCodec.Decode(frame)));
    }

    [Fact]
    public void TeleportAndRepeatedSlackCannotAdvanceTheAcceptedOrigin()
    {
        var budget = new MotionBudget(NetVector3.Zero, 0, 30);
        Assert.False(budget.TryMove(new NetVector3(1000, 0, 0), 1));
        Assert.Equal(NetVector3.Zero, budget.Position);
        Assert.True(budget.TryMove(new NetVector3(20, 0, 0), 1));
        Assert.False(budget.TryMove(new NetVector3(60, 0, 0), 1));
        Assert.Equal(new NetVector3(20, 0, 0), budget.Position);
    }

    [Fact]
    public void WorkBudgetRejectsUnboundedImmediateWork()
    {
        var budget = new WorkBudget();
        Assert.True(budget.TrySpend(8192, 0));
        Assert.False(budget.TrySpend(1, 0));
        Assert.True(budget.TrySpend(4096, 1));
    }

    [Fact]
    public void HoldRejectsInvalidRotationAndVelocity()
    {
        Assert.False(MotionBudget.ValidHold(new HoldMessage(HoldTarget.Rigid(1), HoldPhase.Update, true, NetVector3.Zero, new NetQuaternion(0,0,0,0), NetVector3.Zero)));
        Assert.False(MotionBudget.ValidHold(new HoldMessage(HoldTarget.Rigid(1), HoldPhase.End, false, NetVector3.Zero, NetQuaternion.Identity, new NetVector3(10000,0,0))));
    }
    [Fact]
    public void ReleaseEnvelopeAllowsGravityButRejectsHorizontalTeleport()
    {
        Assert.False(ReleaseEnvelope.Accepts(NetVector3.Zero, new NetVector3(40, 0, 0), NetVector3.Zero, 0, 3));
        Assert.False(ReleaseEnvelope.Accepts(NetVector3.Zero, NetVector3.Zero, new NetVector3(80, 0, 0), 1, 3));
        Assert.True(ReleaseEnvelope.Accepts(new NetVector3(0, 100, 0), new NetVector3(5, 80, 0), new NetVector3(3, -20, 0), 2, 3));
    }

}
