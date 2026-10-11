using System;
using System.IO;
using FamilyTimeMultiplayer.Core.Saves;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class SaveCollectionTests
{
    [Fact]
    public void CollectionSelectionStartsInSoloMode()
    {
        var selection = new SaveCollectionSelection();

        Assert.Equal(SaveCollection.Solo, selection.Current);
    }

    [Fact]
    public void CollectionSelectionCanSwitchBackAndForth()
    {
        var selection = new SaveCollectionSelection();

        selection.Select(SaveCollection.Multiplayer);
        Assert.Equal(SaveCollection.Multiplayer, selection.Current);
        selection.Select(SaveCollection.Solo);
        Assert.Equal(SaveCollection.Solo, selection.Current);
    }

    [Fact]
    public void SoloCollectionKeepsNativeDirectory()
    {
        string nativeDirectory = Path.Combine(Path.GetTempPath(), "FamilyTime", "Sessions");

        string result = SaveCollectionPaths.GetDirectory(nativeDirectory, SaveCollection.Solo, "Multiplayer");

        Assert.Equal(nativeDirectory, result);
    }

    [Fact]
    public void MultiplayerCollectionUsesDedicatedChildDirectory()
    {
        string nativeDirectory = Path.Combine(Path.GetTempPath(), "FamilyTime", "Sessions");

        string result = SaveCollectionPaths.GetDirectory(nativeDirectory, SaveCollection.Multiplayer, "Multiplayer");

        Assert.Equal(Path.Combine(nativeDirectory, "Multiplayer"), result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/path")]
    public void CollectionDirectoryRequiresAnAbsoluteNativePath(string nativeDirectory)
    {
        Assert.Throws<ArgumentException>(() => SaveCollectionPaths.GetDirectory(nativeDirectory, SaveCollection.Solo, "Multiplayer"));
    }

    [Theory]
    [InlineData("ftmp_host_snapshot")]
    [InlineData("FTMP_HOST_SNAPSHOT.world")]
    [InlineData("ftmp_from_host.family")]
    public void InternalTransferNamesAreHidden(string name)
    {
        Assert.True(SaveCollectionPolicy.IsInternalSaveName(name));
    }

    [Theory]
    [InlineData("camp")]
    [InlineData("family-world.world")]
    public void OrdinarySaveNamesRemainVisible(string name)
    {
        Assert.False(SaveCollectionPolicy.IsInternalSaveName(name));
    }
}
