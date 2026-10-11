using System.IO;
using FamilyTimeMultiplayer.Core.Protocol;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class CommandAudioProtocolTests
{
    [Fact]
    public void CommandAudioFrameDecodesTheRequestedCue()
    {
        var frame = new byte[] { 32, 9 };
        CommandAudioMessage message = Assert.IsType<CommandAudioMessage>(MessageCodec.Decode(frame));
        byte[] encoded = MessageCodec.Encode(message);
        Assert.Equal(frame, encoded);
        Assert.Equal(CommandSound.FollowStart, message.Sound);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(255)]
    public void CommandAudioRejectsUnknownCues(byte cue) =>
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(new byte[] { 32, cue }));
}
