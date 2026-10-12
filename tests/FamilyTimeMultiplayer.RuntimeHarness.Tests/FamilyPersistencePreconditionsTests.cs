using System.Collections.Generic;
using FamilyTimeMultiplayer.RuntimeHarness.Contracts;
using Xunit;

namespace FamilyTimeMultiplayer.RuntimeHarness.Tests;

public sealed class FamilyPersistencePreconditionsTests
{
    [Fact]
    public void AcceptsBooleanFactsSerializedByBooleanToString()
    {
        var facts = new Dictionary<string, string>
        {
            ["family.count"] = "2",
            ["family.0.childCount"] = "3",
            ["family.0.mamaAssigned"] = "True",
            ["family.1.childCount"] = "3",
            ["family.1.mamaAssigned"] = "True",
            ["family.childOwnershipUnique"] = "True",
            ["family.totalOwnedChildren"] = "6",
            ["family.liveChildCount"] = "6"
        };

        Assert.Empty(FamilyPersistencePreconditions.FindMissing(facts));
    }
}
