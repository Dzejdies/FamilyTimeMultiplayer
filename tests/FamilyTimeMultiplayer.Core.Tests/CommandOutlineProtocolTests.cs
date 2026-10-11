using System;
using System.IO;
using FamilyTimeMultiplayer.Core.Protocol;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class CommandOutlineProtocolTests
{
    [Fact]
    public void SnapshotDistinguishesSelectedAndConstructionObjectsByNetworkId()
    {
        byte[] frame = { 34, 2,0,0,0, 0, 19,0,0,0, 3, 7,0,0,0 };
        Assert.Equal(frame, MessageCodec.Encode(MessageCodec.Decode(frame)));
    }

    [Fact]
    public void EmptySnapshotRemovesPreviousOutlines()
    {
        byte[] frame = { 34, 0,0,0,0 };
        Assert.Equal(frame, MessageCodec.Encode(MessageCodec.Decode(frame)));
    }
    [Theory]
    [InlineData(CommandOutlineKind.Selected)]
    [InlineData(CommandOutlineKind.Primary)]
    [InlineData(CommandOutlineKind.Dropoff)]
    [InlineData(CommandOutlineKind.Construction)]
    public void StylesPreserveTheExactRigidIdentity(CommandOutlineKind kind)
    {
        var message = new CommandOutlinesMessage(new[] { new CommandOutline(kind, 100001) });
        var decoded = Assert.IsType<CommandOutlinesMessage>(MessageCodec.Decode(MessageCodec.Encode(message)));
        Assert.Equal(kind, Assert.Single(decoded.Outlines).Kind);
        Assert.Equal(100001, Assert.Single(decoded.Outlines).RigidNetId);
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(255, 19)]
    [InlineData(0, -1)]
    public void UnknownStylesAndNegativeIdsAreRejected(byte kind, int netId)
    {
        byte[] frame = { 34, 1,0,0,0, kind, (byte)netId,(byte)(netId >> 8),(byte)(netId >> 16),(byte)(netId >> 24) };
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(frame));
    }

    [Fact]
    public void DuplicateStyleForAnObjectIsRejected()
    {
        byte[] frame = { 34, 2,0,0,0, 0, 19,0,0,0, 0, 19,0,0,0 };
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(frame));
    }

    [Fact]
    public void OneObjectCanBeSelectedAndAnActiveJobTarget()
    {
        var message = new CommandOutlinesMessage(new[] { new CommandOutline(CommandOutlineKind.Selected, 19), new CommandOutline(CommandOutlineKind.Primary, 19) });
        var decoded = Assert.IsType<CommandOutlinesMessage>(MessageCodec.Decode(MessageCodec.Encode(message)));
        Assert.Equal(2, decoded.Outlines.Count);
    }

    [Fact]
    public void SnapshotOwnsItsEntries()
    {
        var entries = new[] { new CommandOutline(CommandOutlineKind.Selected, 19) };
        var message = new CommandOutlinesMessage(entries);
        entries[0] = new CommandOutline(CommandOutlineKind.Construction, 7);
        Assert.Equal(19, Assert.Single(message.Outlines).RigidNetId);
    }
}
