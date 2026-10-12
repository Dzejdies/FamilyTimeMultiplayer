using FamilyTimeMultiplayer.RuntimeHarness.Contracts;
using Xunit;

namespace FamilyTimeMultiplayer.RuntimeHarness.Tests;

public sealed class RuntimeHarnessReadinessTimeoutTests
{
    [Fact]
    public void WorldStartupWaitCoversObservedBuild105LoadTime()
    {
        BoundedWait wait = new(RuntimeHarnessReadinessTimeouts.WorldStartupSeconds);

        Assert.False(wait.IsTimedOut(elapsedSeconds: 100, lastObservation: "native world loading"));
        Assert.True(wait.IsTimedOut(
            elapsedSeconds: RuntimeHarnessReadinessTimeouts.WorldStartupSeconds,
            lastObservation: "native world loading"));
    }
}
