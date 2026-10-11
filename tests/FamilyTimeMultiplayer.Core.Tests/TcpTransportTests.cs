using System;
using System.Diagnostics;
using System.Net;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Transport;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public class TcpTransportTests
{
    private static T WaitFor<T>(Func<(bool, T)> poll)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            (bool done, T value) = poll();
            if (done)
            {
                return value;
            }
            System.Threading.Thread.Sleep(5);
        }
        throw new TimeoutException("Condition not met within 5 s.");
    }

    [Fact]
    public void ExchangesMessagesOverLoopback()
    {
        using var listener = new TcpNetListener(IPAddress.Loopback, 0);
        PendingConnect pending = PendingConnect.Start("127.0.0.1", listener.Port, TimeSpan.FromSeconds(5));
        INetConnection server = WaitFor(() => (listener.TryAccept(out INetConnection c), c));
        INetConnection client = WaitFor(() => (pending.IsCompleted, pending.Connection!));
        Assert.Null(pending.Error);

        client.Send(new HelloMessage(1, "b", "n"));
        var hello = (HelloMessage)WaitFor(() => (server.TryReceive(out INetMessage m), m));
        Assert.Equal("n", hello.PlayerName);

        var big = new byte[ProtocolConstants.WorldChunkBytes];
        new Random(1).NextBytes(big);
        server.Send(new WorldChunkMessage(0, big));
        var chunk = (WorldChunkMessage)WaitFor(() => (client.TryReceive(out INetMessage m), m));
        Assert.Equal(Crc32.Compute(big), Crc32.Compute(chunk.Data));

        client.Close("test done");
        WaitFor(() => (!server.IsOpen, true));
        server.Dispose();
    }

    [Fact]
    public void ReportsConnectFailure()
    {
        using var listener = new TcpNetListener(IPAddress.Loopback, 0);
        int port = listener.Port;
        listener.Dispose();
        PendingConnect pending = PendingConnect.Start("127.0.0.1", port, TimeSpan.FromSeconds(2));
        WaitFor(() => (pending.IsCompleted, true));
        Assert.NotNull(pending.Error);
    }
}
