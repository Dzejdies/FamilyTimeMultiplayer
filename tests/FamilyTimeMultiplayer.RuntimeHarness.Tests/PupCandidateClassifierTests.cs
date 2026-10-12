using FamilyTimeMultiplayer.RuntimeHarness.Contracts;
using Xunit;

namespace FamilyTimeMultiplayer.RuntimeHarness.Tests;

public sealed class PupCandidateClassifierTests
{
    [Fact]
    public void ReportsChildWhenModRejectsItAndNameDoesNotContainPup()
    {
        Assert.True(PupCandidateClassifier.ShouldReport(isChild: true, declaredPup: false, nameLooksLikePup: false, isPup: false));
        Assert.Equal("mod IsPup returned false", PupCandidateClassifier.RejectionReason("false"));
    }

    [Fact]
    public void ReportsDeclaredPupEvenWhenRuntimeEligibilityCouldNotBeEvaluated()
    {
        Assert.True(PupCandidateClassifier.ShouldReport(isChild: false, declaredPup: true, nameLooksLikePup: false, isPup: false));
        Assert.Equal("mod IsPup could not be evaluated for this instance", PupCandidateClassifier.RejectionReason("not-evaluated"));
    }

    [Fact]
    public void DoesNotReportUnrelatedAgent()
    {
        Assert.False(PupCandidateClassifier.ShouldReport(isChild: false, declaredPup: false, nameLooksLikePup: false, isPup: false));
    }

    [Fact]
    public void PreservesEligibilityInvocationErrors()
    {
        Assert.Equal("error:InvalidOperationException:missing config", PupCandidateClassifier.RejectionReason("error:InvalidOperationException:missing config"));
    }
}
