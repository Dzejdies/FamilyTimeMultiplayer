using System.Linq;
using FamilyTimeMultiplayer.Core.Families;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class FamilyWorkerIsolationTests
{
    [Theory]
    [InlineData("Host", 1)]
    [InlineData("Client", 3)]
    public void NativeFollowRangeExcludesForeignUnownedAndUnavailableChildren(string owner, ushort expectedFollower)
    {
        var ledger = new FamilyLedger();
        ledger.Assign(1, "Host");
        ledger.Assign(2, "Host");
        ledger.Assign(3, "Client");
        ledger.Assign(4, "Client");
        ushort[] workerRange = { 1, 2, 3, 4, 5 };
        ushort[] nativeUnavailable = { 2, 4 };

        ushort[] exclusions = ledger.ExcludedWorkersFor(owner, workerRange, nativeUnavailable);

        Assert.Equal(new[] { expectedFollower }, workerRange.Except(exclusions));
        Assert.Equal(exclusions.Length, exclusions.Distinct().Count());
    }

    [Fact]
    public void ReassignedChildLeavesPreviousOwnersFollowRange()
    {
        var ledger = new FamilyLedger();
        ledger.Assign(1, "Host");
        ledger.Assign(2, "Host");
        ushort[] workerRange = { 1, 2 };
        Assert.Empty(ledger.ExcludedWorkersFor("Host", workerRange, new ushort[0]));

        ledger.Assign(2, "Client");

        Assert.Equal(new ushort[] { 2 }, ledger.ExcludedWorkersFor("Host", workerRange, new ushort[0]));
        Assert.Equal(new ushort[] { 1 }, ledger.ExcludedWorkersFor("Client", workerRange, new ushort[0]));
    }
}
