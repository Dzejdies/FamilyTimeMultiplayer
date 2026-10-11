using System;
using System.IO;
using FamilyTimeMultiplayer.Core.Protocol;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class CommandMarkerProtocolTests
{
    [Fact]
    public void FramePreservesAnAttackMarkerAndItsHostIdentity()
    {
        byte[] frame = { 33, 1,0,0,0, 3, 7,0, 0,0,128,63, 0,0,0,64, 0,0,64,64 };
        var message = Assert.IsType<CommandMarkersMessage>(MessageCodec.Decode(frame));
        Assert.Equal((ushort)7, Assert.Single(message.Markers).HostTarget);
        Assert.Equal(frame, MessageCodec.Encode(message));
    }

    [Fact]
    public void EmptySnapshotCanClearCompletedCommands()
    {
        byte[] frame = { 33, 0,0,0,0 };
        Assert.Equal(frame, MessageCodec.Encode(MessageCodec.Decode(frame)));
    }
    [Theory]
    [InlineData(CommandMarkerKind.Goto)]
    [InlineData(CommandMarkerKind.Dropoff)]
    [InlineData(CommandMarkerKind.Construction)]
    [InlineData(CommandMarkerKind.Pickup)]
    public void SiblingMarkerKindsPreserveTheirPositions(CommandMarkerKind kind)
    {
        var marker = new CommandMarker(kind, new NetVector3(4, 62, 8));
        var message = Assert.IsType<CommandMarkersMessage>(MessageCodec.Decode(MessageCodec.Encode(new CommandMarkersMessage(new[] { marker }))));
        Assert.Equal(marker.Kind, Assert.Single(message.Markers).Kind);
        Assert.Equal(marker.Position, Assert.Single(message.Markers).Position);
        Assert.Equal(ushort.MaxValue, Assert.Single(message.Markers).HostTarget);
    }

    [Theory]
    [InlineData(5, 65535)]
    [InlineData(3, 65535)]
    [InlineData(1, 7)]
    public void InvalidKindsAndTargetIdentitiesAreRejected(byte kind, ushort target)
    {
        byte[] frame = { 33, 1,0,0,0, kind, (byte)target,(byte)(target >> 8), 0,0,128,63, 0,0,0,64, 0,0,64,64 };
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(frame));
    }

    [Fact]
    public void NonFinitePositionsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new CommandMarker(CommandMarkerKind.Goto, new NetVector3(float.NaN, 0, 0)));
    }

    [Fact]
    public void SnapshotDoesNotChangeWhenTheCallerReplacesItsMarkers()
    {
        var markers = new[] { new CommandMarker(CommandMarkerKind.Goto, NetVector3.One) };
        var message = new CommandMarkersMessage(markers);
        markers[0] = new CommandMarker(CommandMarkerKind.Pickup, NetVector3.Zero);
        Assert.Equal(CommandMarkerKind.Goto, Assert.Single(message.Markers).Kind);
    }
}
