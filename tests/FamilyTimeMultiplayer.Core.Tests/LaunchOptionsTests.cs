using System;
using FamilyTimeMultiplayer.Core.Launch;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public class LaunchOptionsTests
{
    [Fact]
    public void ParsesJoinEndpointAndName()
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "FamilyTime.exe", "-ftmp-join", "127.0.0.1:4000", "-ftmp-name", "Ola" });
        Assert.Equal((LaunchAction.Join, "127.0.0.1", 4000, "Ola"), (options.Action, options.JoinHost, options.Port, options.PlayerName));
    }

    [Fact]
    public void HostPortIsOptional()
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "-ftmp-host", "-screen-fullscreen", "0" });
        Assert.Equal(LaunchAction.Host, options.Action);
        Assert.Null(options.Port);
    }

    [Fact]
    public void IgnoresUnrelatedArguments() =>
        Assert.Equal(LaunchAction.None, LaunchOptions.Parse(new[] { "-screen-width", "1280" }).Action);

    [Fact]
    public void RejectsHostAndJoinTogether() =>
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(new[] { "-ftmp-host", "-ftmp-join", "a:1" }));

    [Fact]
    public void RejectsInvalidPort() =>
        Assert.Throws<ArgumentException>(() => LaunchOptions.ParseEndpoint("host:99999"));

    [Fact]
    public void ParsesFamilyMode() =>
        Assert.Equal(Protocol.FamilyMode.Separate, LaunchOptions.Parse(new[] { "-ftmp-host", "-ftmp-family", "Separate" }).FamilyMode);

    [Fact]
    public void RejectsUnknownFamilyMode() =>
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(new[] { "-ftmp-family", "solo" }));
}
