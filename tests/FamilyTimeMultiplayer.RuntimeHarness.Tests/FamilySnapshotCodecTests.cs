using System.Collections.Generic;
using FamilyTimeMultiplayer.RuntimeHarness.Contracts;
using Xunit;

namespace FamilyTimeMultiplayer.RuntimeHarness.Tests;

public sealed class FamilySnapshotCodecTests
{
    [Fact]
    public void RoundTripsOnlySemanticFacts()
    {
        var facts = new Dictionary<string, string>
        {
            ["family.semantic.client.children"] = "3",
            ["family.semantic.client.identity"] = "key\tvalue\nnext",
            ["family.runtime.agentIds"] = "101,102,103"
        };

        IReadOnlyDictionary<string, string> restored = FamilySnapshotCodec.Read(FamilySnapshotCodec.Write(facts));

        Assert.Equal("3", restored["family.semantic.client.children"]);
        Assert.Equal("key\tvalue\nnext", restored["family.semantic.client.identity"]);
        Assert.DoesNotContain("family.runtime.agentIds", restored.Keys);
    }

    [Fact]
    public void ComparisonIgnoresRuntimeIdsButDetectsOwnershipChanges()
    {
        var before = new Dictionary<string, string> { ["family.semantic.client.owner"] = "client-key", ["family.runtime.agentIds"] = "12,13,14" };
        var after = new Dictionary<string, string> { ["family.semantic.client.owner"] = "client-key", ["family.runtime.agentIds"] = "91,92,93" };
        Assert.Empty(FamilySnapshotCodec.Compare(
            FamilySnapshotCodec.Read(FamilySnapshotCodec.Write(before)),
            FamilySnapshotCodec.Read(FamilySnapshotCodec.Write(after))));
        after["family.semantic.client.owner"] = "different-key";
        Assert.Single(FamilySnapshotCodec.Compare(
            FamilySnapshotCodec.Read(FamilySnapshotCodec.Write(before)),
            FamilySnapshotCodec.Read(FamilySnapshotCodec.Write(after))));
    }

    [Fact]
    public void ComparisonAcceptsLiveFactsWithAdditionalRuntimeDiagnostics()
    {
        var expected = new Dictionary<string, string> { ["family.semantic.owner.identity"] = "host" };
        var actual = new Dictionary<string, string>
        {
            ["family.semantic.owner.identity"] = "host",
            ["family.runtime.agentIds"] = "91,92,93",
            ["session.isLive"] = "True"
        };

        Assert.Empty(FamilySnapshotCodec.Compare(expected, actual));
    }

    [Fact]
    public void RejectsInvalidAndEmptySnapshots()
    {
        Assert.Throws<System.FormatException>(() => FamilySnapshotCodec.Read("bad-line"));
        Assert.Throws<System.FormatException>(() => FamilySnapshotCodec.Read(""));
    }
}
