using System;
using System.IO;
using FamilyTimeMultiplayer.Core.Protocol;
using Xunit;
using FamilyTimeMultiplayer.Core.Water;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class WaterReplicationTests
{
    [Fact]
    public void WaterViewHasAnExplicitBoundedWorldOrigin()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)36);
        writer.Write(-128);
        writer.Write(32);
        byte[] frame = stream.ToArray();
        Assert.Equal(frame, MessageCodec.Encode(MessageCodec.Decode(frame)));
    }

    [Fact]
    public void CompleteWaterTilePreservesDryCells()
    {
        byte[] frame = TileFrame(0, 32);
        Assert.Equal(frame, MessageCodec.Encode(MessageCodec.Decode(frame)));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2048, 0)]
    [InlineData(int.MinValue, 0)]
    public void InvalidTileCoordinatesAreRejected(int x, int z) =>
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(TileFrame(x, z)));

    [Fact]
    public void WaterDiffSendsDryReplacementAndRefreshesNewViews()
    {
        var tracker = new WaterTileTracker();
        var levels = new ushort[256 * 256];
        levels[19] = 30000;
        Assert.Equal(64, tracker.Changes(new WaterViewMessage(0, 0), levels).Tiles.Count);
        Assert.Empty(tracker.Changes(new WaterViewMessage(0, 0), levels).Tiles);
        levels[19] = 0;
        WaterTilesMessage dry = tracker.Changes(new WaterViewMessage(0, 0), levels);
        Assert.Single(dry.Tiles);
        Assert.Equal((ushort)0, dry.Tiles[0].LevelAt(19));
        tracker.Reset();
        Assert.Equal(64, tracker.Changes(new WaterViewMessage(0, 0), levels).Tiles.Count);
    }

    [Fact]
    public void WaterSnapshotCannotChangeAfterItsSourceIsModified()
    {
        var levels = new ushort[1024];
        levels[0] = 900;
        var tile = new WaterTile(0, 0, levels);
        levels[0] = 0;
        ushort[] copy = tile.CopyLevels();
        copy[0] = 0;
        Assert.Equal((ushort)900, tile.LevelAt(0));
    }

    [Fact]
    public void ChangedViewReplacesEveryTileAndClipsWorldEdges()
    {
        var tracker = new WaterTileTracker();
        var levels = new ushort[256 * 256];
        tracker.Changes(new WaterViewMessage(0, 0), levels);
        Assert.Equal(64, tracker.Changes(new WaterViewMessage(32, 0), levels).Tiles.Count);
        Assert.Equal(32, tracker.Changes(new WaterViewMessage(-2176, -128), levels).Tiles.Count);
    }

    [Fact]
    public void DuplicateTileOriginsAreRejected()
    {
        var tile = new WaterTile(0, 0, new ushort[1024]);
        Assert.Throws<InvalidDataException>(() => new WaterTilesMessage(new[] { tile, tile }));
    }

    private static byte[] TileFrame(int x, int z)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)37);
        writer.Write(1);
        writer.Write(x);
        writer.Write(z);
        for (int i = 0; i < 1024; i++) writer.Write((ushort)(i == 19 ? 30000 : 0));
        return stream.ToArray();
    }
}
