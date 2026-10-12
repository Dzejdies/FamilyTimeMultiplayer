using FamilyTimeMultiplayer.RuntimeHarness.Contracts;
using Xunit;

namespace FamilyTimeMultiplayer.RuntimeHarness.Tests;

public sealed class PupObservationTests
{
    [Fact]
    public void InvalidCandidateDoesNotReportDistanceFromDefaultPosition()
    {
        Assert.Equal("unavailable", PupObservation.FormatCandidateDistance(isValid: false, hasCamera: true, hasCandidatePosition: true, distance: 0));
    }

    [Fact]
    public void ReadinessLogIsEmittedOnlyWhenObservedStateChanges()
    {
        Assert.True(PupObservation.ShouldLogReadiness(previous: null, current: "candidate=none"));
        Assert.False(PupObservation.ShouldLogReadiness(previous: "candidate=none", current: "candidate=none"));
        Assert.True(PupObservation.ShouldLogReadiness(previous: "candidate=none", current: "candidate=wolf_girl_GAT"));
    }

    [Fact]
    public void ReadinessLogKeyIgnoresSmallDistanceAndPositionChanges()
    {
        string first = PupObservation.ReadinessLogKey("candidateValid=True;candidate=Handle(129);isPup=true;distance=1.60;candidateHead=(1,2,3)");
        string second = PupObservation.ReadinessLogKey("candidateValid=True;candidate=Handle(129);isPup=true;distance=1.61;candidateHead=(1.01,2,3)");

        Assert.Equal(first, second);
    }

    [Fact]
    public void ReadinessLogKeyChangesWhenCandidateMovesOutOfRange()
    {
        string inRange = PupObservation.ReadinessLogKey("candidateValid=True;candidate=Handle(129);isPup=true;distance=2.99;candidateHead=(1,2,3)");
        string outOfRange = PupObservation.ReadinessLogKey("candidateValid=True;candidate=Handle(129);isPup=true;distance=3.01;candidateHead=(1,2,3)");

        Assert.NotEqual(inRange, outOfRange);
    }
}
