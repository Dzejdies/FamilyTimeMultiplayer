using System;
using FamilyTimeMultiplayer.RuntimeHarness.Contracts;
using Xunit;

namespace FamilyTimeMultiplayer.RuntimeHarness.Tests;

public sealed class ScenarioReportTests
{
    [Fact]
    public void ReportRequiresAnExplicitTerminalVerdict()
    {
        ScenarioReport report = new ScenarioReport("save-roundtrip", "run-1");
        report.AddFact("host.status", "Live");

        Assert.False(report.IsComplete);
        Assert.Null(report.Verdict);
    }

    [Fact]
    public void ReportPreservesActualObservedFactsAndEvidence()
    {
        ScenarioReport report = new ScenarioReport("save-roundtrip", "run-1");
        report.AddFact("client.family", "missing");
        report.AddEvidence("host.log:line=42");
        report.Finish(ScenarioVerdict.BlockedPrecondition, "client family was not registered");

        Assert.Equal(ScenarioVerdict.BlockedPrecondition, report.Verdict);
        Assert.Contains("\"client.family\":\"missing\"", report.ToJson());
        Assert.Contains("host.log:line=42", report.ToJson());
        Assert.Contains("client family was not registered", report.ToJson());
        Assert.Contains("BLOCKED_PRECONDITION", report.ToJson());
    }

    [Fact]
    public void ReportRejectsConflictingTerminalResults()
    {
        ScenarioReport report = new ScenarioReport("build", "run-2");
        report.Finish(ScenarioVerdict.Fail, "material count did not change");

        Assert.Throws<InvalidOperationException>(() => report.Finish(ScenarioVerdict.Pass, "success"));
    }

    [Fact]
    public void BoundedWaitTimesOutWithLastObservedCondition()
    {
        BoundedWait wait = new BoundedWait(timeoutSeconds: 10);

        Assert.False(wait.IsTimedOut(elapsedSeconds: 9, lastObservation: "client not Live"));
        Assert.True(wait.IsTimedOut(elapsedSeconds: 10, lastObservation: "client not Live"));
        Assert.Equal("client not Live", wait.LastObservation);
    }

    [Fact]
    public void BoundedWaitStartsWhenItsRequiredConditionIsFirstObserved()
    {
        BoundedWait wait = new BoundedWait(timeoutSeconds: 10);

        Assert.False(wait.IsTimedOutAt(nowSeconds: 100, lastObservation: "waiting for Live"));
        Assert.False(wait.IsTimedOutAt(nowSeconds: 109, lastObservation: "waiting for Live"));
        Assert.True(wait.IsTimedOutAt(nowSeconds: 110, lastObservation: "waiting for Live"));
    }
}
