using System;
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
    public void MotionBudgetReportsRejectedMovementWithoutAdvancingItsOrigin()
    {
        var budget = new MotionBudget(NetVector3.Zero, 10, 30, 3);

        MotionMoveDiagnostics assessment = budget.AssessMove(new NetVector3(40, 0, 0), 10.5);

        Assert.Equal(MotionMoveRejection.HorizontalDistanceExceeded, assessment.Rejection);
        Assert.Equal(NetVector3.Zero, assessment.Origin);
        Assert.Equal(new NetVector3(40, 0, 0), assessment.Target);
        Assert.Equal(0.5, assessment.ElapsedSeconds, precision: 5);
        Assert.Equal(40, assessment.HorizontalDistance, precision: 3);
        Assert.Equal(18, assessment.HorizontalAllowance, precision: 3);
        Assert.Equal(NetVector3.Zero, budget.Position);
    }

    [Fact]
    public void MotionBudgetStillRejectsUnauthorizedMovementAfterAssessment()
    {
        var budget = new MotionBudget(NetVector3.Zero, 0, 30, 3);

        MotionMoveDiagnostics assessment = budget.AssessMove(new NetVector3(1000, 0, 0), 1);
        MotionPoseAssessment poseAssessment = budget.AssessPose(
            new ClientPoseMessage(new NetVector3(1000, 0, 0), 0, 0, new NetVector3(0, 0, 50)), 1, 30);

        Assert.Equal(MotionMoveRejection.HorizontalDistanceExceeded, assessment.Rejection);
        Assert.Equal(MotionPoseRejection.PositionBudgetExceeded, poseAssessment.Rejection);
        Assert.False(budget.TryMove(new NetVector3(1000, 0, 0), 1));
        Assert.Equal(NetVector3.Zero, budget.Position);
    }

    [Fact]
    public void MotionBudgetUsesAcceptedDisplacementInsteadOfRejectingAReportedSpeedSpike()
    {
        var budget = new MotionBudget(new NetVector3(2, 59, 0), 10, 30, 5);
        var pose = new ClientPoseMessage(
            new NetVector3(0.04f, 57.88f, 1.87f),
            0,
            0,
            new NetVector3(1.23f, 29.40f, 48.95f));

        MotionPoseAssessment assessment = budget.AssessPose(pose, 10.1, 30);

        Assert.Equal(MotionPoseRejection.None, assessment.Rejection);
        Assert.Equal(30, assessment.HorizontalSpeedLimit);
        Assert.InRange(assessment.HorizontalSpeed, 48.9f, 49.1f);
        Assert.Equal(MotionMoveRejection.None, assessment.Movement.Rejection);
        NetVector3 authorizedVelocity = assessment.Movement.GetAuthorizedVelocity(30);
        Assert.InRange(MathF.Sqrt(authorizedVelocity.X * authorizedVelocity.X + authorizedVelocity.Z * authorizedVelocity.Z), 0, 30.001f);
        Assert.Equal(new NetVector3(2, 59, 0), budget.Position);
    }

    [Fact]
    public void MotionBudgetAcceptsLegitimateMovementNearTheHorizontalSpeedLimit()
    {
        var budget = new MotionBudget(NetVector3.Zero, 0, 30, 5);
        var pose = new ClientPoseMessage(new NetVector3(29.9f, 0, 0), 0, 0, new NetVector3(29.9f, 0, 0));

        MotionPoseAssessment assessment = budget.AssessPose(pose, 1, 30);

        Assert.True(assessment.Accepted);
        Assert.True(budget.TryMove(pose.Position, 1));
        Assert.Equal(pose.Position, budget.Position);
    }

    [Fact]
    public void MotionBudgetUsesObservedDisplacementForInconsistentClientVelocity()
    {
        var budget = new MotionBudget(NetVector3.Zero, 0, 30, 5);
        var pose = new ClientPoseMessage(new NetVector3(1, 0, 0), 0, 0, new NetVector3(0, 0, 80));

        MotionPoseAssessment assessment = budget.AssessPose(pose, 1, 30);
        NetVector3 authorizedVelocity = assessment.Movement.GetAuthorizedVelocity(30);

        Assert.True(assessment.Accepted);
        Assert.Equal(new NetVector3(1, 0, 0), authorizedVelocity);
        Assert.Equal(NetVector3.Zero, budget.Position);
    }

    [Fact]
    public void MotionBudgetAcceptsSteadyBurstsAtShortElapsedIntervals()
    {
        var budget = new MotionBudget(NetVector3.Zero, 0, 30, 5);

        for (int update = 1; update <= 10; update++)
        {
            var position = new NetVector3(update * 0.029f, 0, 0);
            Assert.True(budget.TryMove(position, update * 0.01));
        }

        Assert.Equal(new NetVector3(0.29f, 0, 0), budget.Position);
    }

    [Fact]
    public void MotionBudgetStartsAtTheReboundAvatarPositionAfterReconnect()
    {
        var reboundPosition = new NetVector3(12, 59, -4);
        var budget = new MotionBudget(reboundPosition, 50, 30, 5);

        Assert.True(budget.TryMove(new NetVector3(12.3f, 59, -4), 50.01));
        Assert.False(budget.TryMove(new NetVector3(30, 59, -4), 50.02));
        Assert.Equal(new NetVector3(12.3f, 59, -4), budget.Position);
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
