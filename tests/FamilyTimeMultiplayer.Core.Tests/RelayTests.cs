using System;
using System.Diagnostics;
using System.Net;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Transport;
using FamilyTimeMultiplayer.Core.Transport.Relay;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public class RelayTests
{
    private static T WaitFor<T>(Func<(bool, T)> poll, double seconds = 5)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            (bool done, T value) = poll();
            if (done)
            {
                return value;
            }
            System.Threading.Thread.Sleep(5);
        }
        throw new TimeoutException($"Condition not met within {seconds} s.");
    }

    private static RelayServer StartRelay() => new RelayServer(IPAddress.Loopback, 0, _ => { });

    private static RelayHostListener StartHost(RelayServer relay)
    {
        var host = new RelayHostListener("127.0.0.1", relay.Port);
        WaitFor(() => (host.State != RelayHostState.Connecting, 0));
        Assert.Equal(RelayHostState.Ready, host.State);
        return host;
    }

    private static (INetConnection Host, INetConnection Client) Join(RelayHostListener host, int relayPort, string code)
    {
        PendingConnect pending = PendingConnect.StartViaRelay("127.0.0.1", relayPort, code, TimeSpan.FromSeconds(5));
        INetConnection client = WaitFor(() => (pending.IsCompleted, pending.Connection!));
        Assert.Null(pending.Error);
        INetConnection server = WaitFor(() => (host.TryAccept(out INetConnection c), c));
        return (server, client);
    }

    [Theory]
    [InlineData("k7q m2x", "K7Q-M2X")]
    [InlineData("K7QM2X", "K7Q-M2X")]
    [InlineData(" k7q-m2x ", "K7Q-M2X")]
    public void NormalizesRoomCodes(string typed, string expected)
    {
        Assert.True(RoomCode.TryNormalize(typed, out string code));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData("O0I1LL")]
    [InlineData("ABC")]
    [InlineData("192.168.1.5")]
    [InlineData(null)]
    public void RejectsNonCodes(string? typed) => Assert.False(RoomCode.TryNormalize(typed, out _));

    [Fact]
    public void GeneratedCodesAreValid()
    {
        for (int i = 0; i < 200; i++)
        {
            string code = RoomCode.Generate();
            Assert.True(RoomCode.TryNormalize(code, out string normalized));
            Assert.Equal(code, normalized);
        }
    }

    [Fact]
    public void RelaysGameFramesBothWays()
    {
        using RelayServer relay = StartRelay();
        using RelayHostListener host = StartHost(relay);
        (INetConnection server, INetConnection client) = Join(host, relay.Port, host.Code!.ToLowerInvariant());

        client.Send(new HelloMessage(ProtocolConstants.Version, "build", "Ola"));
        Assert.Equal("Ola", ((HelloMessage)WaitFor(() => (server.TryReceive(out INetMessage m), m))).PlayerName);

        var chunk = new byte[ProtocolConstants.WorldChunkBytes];
        new Random(7).NextBytes(chunk);
        server.Send(new WorldChunkMessage(1024, chunk));
        var received = (WorldChunkMessage)WaitFor(() => (client.TryReceive(out INetMessage m), m));
        Assert.Equal(1024, received.Offset);
        Assert.Equal(chunk, received.Data);
    }

    [Fact]
    public void TwoPlayersGetSeparateConnections()
    {
        using RelayServer relay = StartRelay();
        using RelayHostListener host = StartHost(relay);
        (INetConnection serverA, INetConnection clientA) = Join(host, relay.Port, host.Code!);
        (INetConnection serverB, INetConnection clientB) = Join(host, relay.Port, host.Code!);

        clientA.Send(new HelloMessage(1, "b", "A"));
        clientB.Send(new HelloMessage(1, "b", "B"));
        Assert.Equal("A", ((HelloMessage)WaitFor(() => (serverA.TryReceive(out INetMessage m), m))).PlayerName);
        Assert.Equal("B", ((HelloMessage)WaitFor(() => (serverB.TryReceive(out INetMessage m), m))).PlayerName);
    }

    [Fact]
    public void UnknownRoomIsRefused()
    {
        using RelayServer relay = StartRelay();
        PendingConnect pending = PendingConnect.StartViaRelay("127.0.0.1", relay.Port, "ABC-DEF", TimeSpan.FromSeconds(5));
        WaitFor(() => (pending.IsCompleted, 0));
        Assert.Null(pending.Connection);
        Assert.Contains("no room ABC-DEF", pending.Error);
    }

    [Fact]
    public void RoomClosesWhenHostLeaves()
    {
        using RelayServer relay = StartRelay();
        RelayHostListener host = StartHost(relay);
        string code = host.Code!;
        host.Dispose();
        WaitFor(() => (relay.RoomCount == 0, 0));
        PendingConnect pending = PendingConnect.StartViaRelay("127.0.0.1", relay.Port, code, TimeSpan.FromSeconds(5));
        WaitFor(() => (pending.IsCompleted, 0));
        Assert.Contains($"no room {code}", pending.Error);
    }

    [Fact]
    public void ClosingOneSideClosesTheOther()
    {
        using RelayServer relay = StartRelay();
        using RelayHostListener host = StartHost(relay);
        (INetConnection server, INetConnection client) = Join(host, relay.Port, host.Code!);
        client.Close("bye");
        WaitFor(() => (!server.IsOpen, 0));
    }

    [Fact]
    public void JoinTargetUsesRelayForRoomCodes()
    {
        JoinTarget target = JoinTarget.Parse("k7q-m2x", "relay.example.org");
        Assert.True(target.ViaRelay);
        Assert.Equal(("relay.example.org", RelayWire.DefaultPort, "K7Q-M2X"), (target.Host, target.Port, target.RoomCode));
    }

    [Fact]
    public void JoinTargetUsesDirectAddressOtherwise()
    {
        JoinTarget target = JoinTarget.Parse("192.168.1.5:28000", "relay.example.org:9000");
        Assert.False(target.ViaRelay);
        Assert.Equal(("192.168.1.5", 28000), (target.Host, target.Port));
        Assert.Equal(ProtocolConstants.DefaultPort, JoinTarget.Parse("10.0.0.2", null).Port);
    }

    [Fact]
    public void RoomCodeWithoutRelayIsRejected() =>
        Assert.Throws<ArgumentException>(() => JoinTarget.Parse("K7Q-M2X", " "));

    [Fact]
    public void LaunchJoinKeepsRoomCodeText()
    {
        Launch.LaunchOptions options = Launch.LaunchOptions.Parse(new[] { "-ftmp-join", "K7Q-M2X", "-ftmp-relay", "127.0.0.1:27780" });
        Assert.Equal("K7Q-M2X", options.JoinText);
        Assert.Equal("127.0.0.1:27780", options.RelayAddress);
    }
}
