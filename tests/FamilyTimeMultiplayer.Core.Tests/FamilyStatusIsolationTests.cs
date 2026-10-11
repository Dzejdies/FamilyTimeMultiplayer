using System.IO;
using FamilyTimeMultiplayer.Core.Protocol;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class FamilyStatusIsolationTests
{
    [Fact]
    public void FamilyStatusCarriesOnlyTheRecipientsMembers()
    {
        byte[] frame = { 20, 2,0,0,0, 1,0,0,0, 2,0,0,0, 2,0,0,0, 7,0, 19,0 };
        var message = Assert.IsType<FamilyStatusMessage>(MessageCodec.Decode(frame));
        Assert.Equal(frame, MessageCodec.Encode(message));
        Assert.Equal(new ushort[] { 7, 19 }, message.Members);
    }
    [Fact]
    public void SameCountsCanReplaceTheRecipientsChildren()
    {
        var message = new FamilyStatusMessage(2, 1, 2, new ushort[] { 8, 20 });
        var decoded = Assert.IsType<FamilyStatusMessage>(MessageCodec.Decode(MessageCodec.Encode(message)));
        Assert.Equal(new ushort[] { 8, 20 }, decoded.Members);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(65535)]
    public void DuplicateAndNullHandlesAreRejected(ushort second)
    {
        byte[] frame = { 20, 2,0,0,0, 1,0,0,0, 2,0,0,0, 2,0,0,0, 7,0, (byte)second,(byte)(second >> 8) };
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(frame));
    }

    [Fact]
    public void SharedFamilyKeepsNativeVisibility()
    {
        var message = new FamilyStatusMessage(6, 1, 4);
        var decoded = Assert.IsType<FamilyStatusMessage>(MessageCodec.Decode(MessageCodec.Encode(message)));
        Assert.Null(decoded.Members);
    }
}
