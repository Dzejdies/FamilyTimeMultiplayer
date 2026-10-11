using System.Collections.Generic;
using FamilyTimeMultiplayer.Core.Interpolation;
using FamilyTimeMultiplayer.Core.Protocol;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public class SnapshotInterpolatorTests
{
    private static IEnumerable<KeyValuePair<ushort, NetPose>> At(float x, ushort key = 1) =>
        new[] { new KeyValuePair<ushort, NetPose>(key, new NetPose(new NetVector3(x, 0f, 0f), NetQuaternion.Identity, NetVector3.One)) };

    [Fact]
    public void SamplesMidpointBetweenSnapshotsAfterDelay()
    {
        var interpolator = new SnapshotInterpolator<ushort>(interpolationDelaySeconds: 0.1);
        interpolator.Push(0.0, 100.0, At(0f));
        interpolator.Push(0.1, 100.1, At(10f));
        var output = new List<InterpolatedEntry<ushort>>();

        interpolator.Sample(0.15, output);

        InterpolatedEntry<ushort> agent = Assert.Single(output);
        Assert.Equal(5f, agent.Pose.Position.X, precision: 3);
        Assert.Equal(100f, agent.Velocity.X, precision: 1);
    }

    [Fact]
    public void HoldsLatestPoseWhenRenderTimePassesBuffer()
    {
        var interpolator = new SnapshotInterpolator<ushort>(0.0);
        interpolator.Push(0.0, 5.0, At(1f));
        interpolator.Push(0.1, 5.1, At(2f));
        var output = new List<InterpolatedEntry<ushort>>();

        interpolator.Sample(10.0, output);

        Assert.Equal(2f, Assert.Single(output).Pose.Position.X, precision: 3);
    }

    [Fact]
    public void IgnoresOutOfOrderSnapshots()
    {
        var interpolator = new SnapshotInterpolator<ushort>(0.0);
        interpolator.Push(0.0, 5.0, At(1f));
        interpolator.Push(0.0, 4.0, At(99f));
        Assert.Equal(1, interpolator.BufferedSnapshotCount);
    }

    [Fact]
    public void WorksWithIntKeys()
    {
        var interpolator = new SnapshotInterpolator<int>(0.0);
        interpolator.Push(0.0, 1.0, new[] { new KeyValuePair<int, NetPose>(70000, new NetPose(NetVector3.One, NetQuaternion.Identity, NetVector3.One)) });
        var output = new List<InterpolatedEntry<int>>();
        interpolator.Sample(0.0, output);
        Assert.Equal(70000, Assert.Single(output).Key);
    }
}
