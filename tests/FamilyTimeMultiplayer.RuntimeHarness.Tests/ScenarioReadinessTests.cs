using FamilyTimeMultiplayer.Diagnostics;
using FamilyTimeMultiplayer.RuntimeHarness.Contracts;
using Xunit;

namespace FamilyTimeMultiplayer.RuntimeHarness.Tests;

public sealed class ScenarioReadinessTests
{
    [Fact]
    public void SeparateFamilyScenarioDoesNotWaitForUnrelatedFollowerCounts()
    {
        Assert.False(ScenarioReadiness.RequiresFollowerBootstrap(separateFamilyScenario: true, followerCount: 0));
    }

    [Fact]
    public void CommandScenarioStillWaitsForItsFollowerPrerequisite()
    {
        Assert.True(ScenarioReadiness.RequiresFollowerBootstrap(separateFamilyScenario: false, followerCount: 1));
        Assert.False(ScenarioReadiness.RequiresFollowerBootstrap(separateFamilyScenario: false, followerCount: 2));
    }

    [Fact]
    public void ReadinessWaitHasAnExplicitBound()
    {
        Assert.False(ScenarioReadiness.HasTimedOut(elapsedSeconds: 89, timeoutSeconds: 90));
        Assert.True(ScenarioReadiness.HasTimedOut(elapsedSeconds: 90, timeoutSeconds: 90));
    }

    [Theory]
    [InlineData((byte)118, 59)]
    [InlineData((byte)119, 60)]
    public void GridHeightUsesNativeHalfMeterHeightfieldScale(byte rawHeight, int expectedGridY)
    {
        Assert.Equal(expectedGridY, ScenarioReadiness.GridHeight(rawHeight));
    }

    [Theory]
    [InlineData(true, false, 2, 1, true)]
    [InlineData(true, false, 2, 0, false)]
    [InlineData(false, true, 2, 0, true)]
    [InlineData(false, false, 2, 0, false)]
    [InlineData(false, true, 1, 0, false)]
    public void LivePairUsesHostRemoteLivePlayersAndClientLivePhase(bool isHost, bool sessionIsLive, int rosterCount, int liveRemotePlayers, bool expected)
    {
        Assert.Equal(expected, SessionReadiness.HasLivePair(isHost, sessionIsLive, rosterCount, liveRemotePlayers));
    }
}
