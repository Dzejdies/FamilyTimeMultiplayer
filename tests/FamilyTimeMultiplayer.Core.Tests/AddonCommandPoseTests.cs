using System.IO;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Safety;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class AddonCommandPoseTests
{
    [Fact]
    public void SignedLookChangesOnlyCommandDirection()
    {
        var canonical = new ClientPoseMessage(new NetVector3(10, 80, 20), 15, 0, new NetVector3(1, 2, 3));
        var client = new ClientPoseMessage(new NetVector3(900, 2, 900), 45, 70, new NetVector3(90, 0, 0));
        ClientPoseMessage composed = AddonCommandPose.Compose(canonical, client);
        Assert.Equal(canonical.Position, composed.Position);
        Assert.Equal(canonical.Velocity, composed.Velocity);
        Assert.Equal(client.YawDegrees, composed.YawDegrees);
        Assert.Equal(client.PitchDegrees, composed.PitchDegrees);
    }

    [Theory]
    [InlineData(float.NaN, 0)]
    [InlineData(0, float.PositiveInfinity)]
    [InlineData(361, 0)]
    [InlineData(0, 91)]
    public void InvalidSignedLookIsRejected(float yaw, float pitch)
    {
        var canonical = new ClientPoseMessage(NetVector3.Zero, 0, 0, NetVector3.Zero);
        var client = new ClientPoseMessage(NetVector3.Zero, yaw, pitch, NetVector3.Zero);
        Assert.Throws<InvalidDataException>(() => AddonCommandPose.Compose(canonical, client));
    }

    [Fact]
    public void CurrentHostMotionAndPreviouslyAuthenticatedLookComposeIndependently()
    {
        var look = new ClientPoseMessage(NetVector3.Zero, 25, 55, NetVector3.Zero);
        var moved = new ClientPoseMessage(new NetVector3(20, 100, 30), 90, 0, new NetVector3(5, 0, 0));
        ClientPoseMessage composed = AddonCommandPose.Compose(moved, look);
        Assert.Equal(moved.Position, composed.Position);
        Assert.Equal(moved.Velocity, composed.Velocity);
        Assert.Equal(25, composed.YawDegrees);
        Assert.Equal(55, composed.PitchDegrees);
    }

    [Fact]
    public void MissingLookKeepsCanonicalDefault()
    {
        var canonical = new ClientPoseMessage(NetVector3.Zero, 90, 0, NetVector3.Zero);
        Assert.Same(canonical, AddonCommandPose.Compose(canonical, null));
    }
}
