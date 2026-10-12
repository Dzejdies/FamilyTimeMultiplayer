using FamilyTimeMultiplayer.Diagnostics;
using FamilyTimeMultiplayer.UI;
using System;
using Xunit;

namespace FamilyTimeMultiplayer.RuntimeHarness.Tests;

public sealed class NativeInterventionCleanupTests
{
    [Theory]
    [InlineData("idle")]
    [InlineData("walk-circle")]
    [InlineData("menu-shot")]
    public void OrdinaryScenariosDoNotRequestDiagnosticHarmonyProbes(string scenario)
    {
        Assert.Empty(ScenarioDiagnosticPatchPlan.ForScenario(scenario));
    }

    [Fact]
    public void WorldStateProbesAreRequestedOnlyByWorldDiagnosticScenarios()
    {
        Assert.Equal(
            new[] { DiagnosticProbe.Water, DiagnosticProbe.Lightning, DiagnosticProbe.Animation },
            ScenarioDiagnosticPatchPlan.ForScenario("mp-world"));
        Assert.Equal(
            new[] { DiagnosticProbe.Water, DiagnosticProbe.Lightning, DiagnosticProbe.Animation },
            ScenarioDiagnosticPatchPlan.ForScenario("mp-sync"));
    }

    [Fact]
    public void ScenarioSpecificAudioAndMarkerProbesStayOptIn()
    {
        Assert.Equal(new[] { DiagnosticProbe.DrawMarker }, ScenarioDiagnosticPatchPlan.ForScenario("mp-markers"));
        Assert.Equal(new[] { DiagnosticProbe.CommandAudio }, ScenarioDiagnosticPatchPlan.ForScenario("mp-audio"));
        Assert.Equal(
            new[] { DiagnosticProbe.Water, DiagnosticProbe.Lightning, DiagnosticProbe.Animation, DiagnosticProbe.CommandAudio },
            ScenarioDiagnosticPatchPlan.ForScenario("mp-audio-weather"));
    }

    [Fact]
    public void WorldPauseLeaseCanBeReleasedByControllerAndCoroutineWithoutDoubleDisposal()
    {
        var scope = new CountingDisposable();
        var lease = new WorldPauseScopeLease(scope);

        lease.Dispose();
        lease.Dispose();

        Assert.Equal(1, scope.DisposeCount);
    }

    private sealed class CountingDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }
}
