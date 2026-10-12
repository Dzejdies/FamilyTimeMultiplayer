using System;
using System.IO;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Replication;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class GenericStateReplicationTests
{
    [Fact]
    public void GenericStateMessageRoundTripsItsVersionedEnvelope()
    {
        var identity = new NetworkEntityId(7, new EntityKind("test.counter"), 42);
        var message = new GenericEntityStateMessage(identity, 1, new EntityStateDomain("test.counter"), 3, 11, new byte[] { 4, 5, 6 });

        GenericEntityStateMessage decoded = Assert.IsType<GenericEntityStateMessage>(
            MessageCodec.Decode(MessageCodec.Encode(message)));

        Assert.Equal(identity, decoded.Identity);
        Assert.Equal(1UL, decoded.EntityLifecycleRevision);
        Assert.Equal(new EntityStateDomain("test.counter"), decoded.StateDomain);
        Assert.Equal(3, decoded.SchemaVersion);
        Assert.Equal(11UL, decoded.StateRevision);
        Assert.Equal(new byte[] { 4, 5, 6 }, decoded.Payload.ToArray());
    }

    [Fact]
    public void GenericStateMessageRejectsInvalidHeadersAndOversizedPayloads()
    {
        var identity = new NetworkEntityId(7, new EntityKind("test.counter"), 42);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GenericEntityStateMessage(identity, 1, new EntityStateDomain("test.counter"), 0, 1, Array.Empty<byte>()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GenericEntityStateMessage(identity, 1, new EntityStateDomain("test.counter"), 1, 0, Array.Empty<byte>()));
        Assert.Throws<ArgumentException>(() =>
            new GenericEntityStateMessage(identity, 1, new EntityStateDomain("test.counter"), 1, 1,
                new byte[GenericEntityStateMessage.MaxPayloadBytes + 1]));
    }

    [Fact]
    public void GenericStateMessageRejectsUnsupportedEnvelopeVersion()
    {
        var identity = new NetworkEntityId(7, new EntityKind("test.counter"), 42);
        byte[] frame = MessageCodec.Encode(new GenericEntityStateMessage(
            identity, 1, new EntityStateDomain("test.counter"), 1, 1, new byte[] { 1 }));
        frame[1] = 255;

        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(frame));
    }

    [Fact]
    public void GenericStateMessageClonesItsPayloadAtConstruction()
    {
        byte[] payload = { 4, 5, 6 };
        var message = new GenericEntityStateMessage(
            new NetworkEntityId(7, new EntityKind("test.counter"), 42),
            1, new EntityStateDomain("test.counter"), 1, 1, payload);

        payload[0] = 99;

        Assert.Equal(new byte[] { 4, 5, 6 }, message.Payload.ToArray());
    }

    [Fact]
    public void ProtocolNegotiationRejectsPeersWithoutGenericStateEnvelopeSupport()
    {
        Assert.True(ProtocolConstants.IsCompatibleVersion(ProtocolConstants.Version));
        Assert.False(ProtocolConstants.IsCompatibleVersion(ProtocolConstants.Version - 1));
    }
}
