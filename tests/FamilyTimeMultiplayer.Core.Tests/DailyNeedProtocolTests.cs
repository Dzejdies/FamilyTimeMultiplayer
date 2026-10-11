using System.IO;
using FamilyTimeMultiplayer.Core.Protocol;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class DailyNeedProtocolTests
{
    [Fact]
    public void DayStatePreservesTheHostsThirstSetting()
    {
        byte[] frame = { 11, 2,0,0,0, 0,0,0,0, 0,0,0,0, 0,0,0,0, 0, 1 };
        var message = Assert.IsType<DayStateMessage>(MessageCodec.Decode(frame));
        Assert.Equal(frame, MessageCodec.Encode(message));
        Assert.Equal(DailyNeed.Water, message.Need);
    }
    [Fact]
    public void DailyNeedSwitchesBackToFood()
    {
        var decoded = Assert.IsType<DayStateMessage>(MessageCodec.Decode(MessageCodec.Encode(new DayStateMessage(3, 0, 0, 0, false, DailyNeed.Food))));
        Assert.Equal(DailyNeed.Food, decoded.Need);
    }

    [Fact]
    public void UnknownDailyNeedIsRejected()
    {
        byte[] frame = { 11, 2,0,0,0, 0,0,0,0, 0,0,0,0, 0,0,0,0, 0, 2 };
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(frame));
    }
}
