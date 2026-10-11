using FamilyTimeMultiplayer.Core.Protocol;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public class GameBuildCompatibilityTests
{
    [Theory]
    [InlineData("7ddcd8f6e25c4b7a9d21e4d54ebc8d90", "49296d0c647547449c6e7119d4ecdf5b")]
    [InlineData("7ddcd8f6e25c4b7a9d21e4d54ebc8d90", "62d864033bfd4caeb6e1ba96959d0b1b")]
    [InlineData("49296d0c647547449c6e7119d4ecdf5b", "62d864033bfd4caeb6e1ba96959d0b1b")]
    public void Build91PlatformArtifactsAreCompatible(string hostBuild, string clientBuild)
    {
        Assert.True(GameBuildCompatibility.AreCompatible(hostBuild, clientBuild));
        Assert.True(GameBuildCompatibility.AreCompatible(clientBuild, hostBuild));
    }

    [Fact]
    public void IdenticalUnrecognizedBuildIdsRemainCompatible() =>
        Assert.True(GameBuildCompatibility.AreCompatible("legacy-build", "legacy-build"));

    [Fact]
    public void UnrecognizedBuildCannotJoinKnownBuildFamily() =>
        Assert.False(GameBuildCompatibility.AreCompatible("7ddcd8f6e25c4b7a9d21e4d54ebc8d90", "unrecognized-build"));

    [Fact]
    public void DifferentUnrecognizedBuildsRemainIncompatible() =>
        Assert.False(GameBuildCompatibility.AreCompatible("legacy-build-a", "legacy-build-b"));
}
