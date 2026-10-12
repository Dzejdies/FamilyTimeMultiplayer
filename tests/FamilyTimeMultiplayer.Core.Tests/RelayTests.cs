using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Transport;
using FamilyTimeMultiplayer.Core.Transport.Relay;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public class RelayTests
{
    private const int ConnectionProcessingDelayMilliseconds = 250;
    private const int ExcessConnectionResponseTimeoutMilliseconds = 3000;
    private const int ActiveConnectionsAfterJoin = 3;
    private static readonly TimeSpan RoomRegistrationTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RoomSetupReleaseTimeout = TimeSpan.FromSeconds(5);

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
    public void KeepsPairedRelayConnectionOpenAfterHandshakeReturns()
    {
        using RelayServer relay = StartRelay();
        using RelayHostListener host = StartHost(relay);
        (INetConnection server, INetConnection client) = Join(host, relay.Port, host.Code!);
        System.Threading.Thread.Sleep(ConnectionProcessingDelayMilliseconds);
        Assert.Equal(ActiveConnectionsAfterJoin, relay.ActiveConnectionCount);

        client.Send(new HelloMessage(ProtocolConstants.Version, "build", "Ola"));

        HelloMessage received = (HelloMessage)WaitFor(() => (server.TryReceive(out INetMessage message), message));
        Assert.Equal("Ola", received.PlayerName);

        server.Send(new HelloMessage(ProtocolConstants.Version, "build", "Host"));

        HelloMessage returned = (HelloMessage)WaitFor(() => (client.TryReceive(out INetMessage message), message));
        Assert.Equal("Host", returned.PlayerName);
    }

    [Fact]
    public void RefusesConnectionsBeyondTheConcurrentConnectionLimit()
    {
        using RelayServer relay = StartRelay();
        var stalledClients = new TcpClient[RelayServer.MaxConnections];
        try
        {
            for (int index = 0; index < stalledClients.Length; index++)
            {
                stalledClients[index] = new TcpClient();
                stalledClients[index].Connect(IPAddress.Loopback, relay.Port);
            }
            WaitFor(() => (relay.ActiveConnectionCount == stalledClients.Length, 0));

            using var excessClient = new TcpClient();
            excessClient.ReceiveTimeout = ExcessConnectionResponseTimeoutMilliseconds;
            excessClient.Connect(IPAddress.Loopback, relay.Port);

            Assert.Equal(-1, excessClient.GetStream().ReadByte());
        }
        finally
        {
            foreach (TcpClient? client in stalledClients)
            {
                client?.Dispose();
            }
        }
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
    public void RemovesRoomWhenHostDisconnectsDuringRoomHandshake()
    {
        using var roomAllocated = new ManualResetEventSlim(false);
        using var continueHandshake = new ManualResetEventSlim(false);
        using var relay = new RelayServer(IPAddress.Loopback, 0, message =>
        {
            if (!message.Contains("allocated", StringComparison.Ordinal)) return;
            roomAllocated.Set();
            continueHandshake.Wait(RoomSetupReleaseTimeout);
        });
        using (var client = new TcpClient())
        {
            client.Connect(IPAddress.Loopback, relay.Port);
            client.Client.LingerState = new LingerOption(true, 0);
            byte[] request = System.Text.Encoding.ASCII.GetBytes("FTMPR1 HOST\n");
            client.GetStream().Write(request, 0, request.Length);
        }

        try
        {
            Assert.True(roomAllocated.Wait(RoomRegistrationTimeout));
            Assert.Equal(1, relay.RoomCount);
        }
        finally
        {
            continueHandshake.Set();
        }
        WaitFor(() => (relay.RoomCount == 0, 0));
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
