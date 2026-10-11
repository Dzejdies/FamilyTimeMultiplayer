using System;
using System.IO;
using System.Net;
using System.Threading;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Transport;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class NetworkSafetyTests
{
    [Fact]
    public void NonFinitePitchIsRejectedAtTheProtocolBoundary()
    {
        var message = new ClientPoseMessage(NetVector3.Zero, 0, float.NaN, NetVector3.Zero);
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(MessageCodec.Encode(message)));
    }

    [Fact]
    public void OversizedHandshakeCannotReachRosterAndLogs()
    {
        var message = new HelloMessage(ProtocolConstants.Version, "build", new string('x', 100000));
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(MessageCodec.Encode(message)));
    }

    [Fact]
    public void UnconsumedLoopbackTrafficCannotGrowTheInboxWithoutLimit()
    {
        using var listener = new TcpNetListener(IPAddress.Loopback, 0);
        using var sender = new System.Net.Sockets.TcpClient();
        sender.Connect(IPAddress.Loopback, listener.Port);
        INetConnection? server = null;
        for (int i = 0; i < 1000 && server == null; i++) { if (listener.TryAccept(out INetConnection c)) server = c; else Thread.Sleep(5); }
        Assert.NotNull(server);
        using INetConnection receiver = server!;
        ((TcpNetConnection)receiver).Admit();
        using var writer = new BinaryWriter(sender.GetStream());
        byte[] frame = MessageCodec.Encode(new SignedClientMessage(1, MessageCodec.Encode(new HelloMessage(1, "build", "peer")), new byte[256]));
        for (int i = 0; i < 1500; i++) { writer.Write(frame.Length); writer.Write(frame); }
        writer.Flush();
        object queue = receiver.GetType().GetField("_incoming", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(receiver)!;
        Assert.True(SpinWait.SpinUntil(() => (int)queue.GetType().GetProperty("Count")!.GetValue(queue)! >= 1024, TimeSpan.FromSeconds(5)));
        int count = (int)queue.GetType().GetProperty("Count")!.GetValue(queue)!;
        Assert.InRange(count, 1, 1024);
        Assert.True(receiver.TryReceive(out _));
    }
    [Fact]
    public void UnacceptedConnectionsCannotGrowTheListenerWithoutLimit()
    {
        using var listener = new TcpNetListener(IPAddress.Loopback, 0);
        var clients = new System.Collections.Generic.List<System.Net.Sockets.TcpClient>();
        try
        {
            for (int i = 0; i < 100; i++)
            {
                var client = new System.Net.Sockets.TcpClient();
                client.Connect(IPAddress.Loopback, listener.Port);
                clients.Add(client);
            }
            object queue = listener.GetType().GetField("_accepted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(listener)!;
            Assert.True(SpinWait.SpinUntil(() => (int)queue.GetType().GetProperty("Count")!.GetValue(queue)! >= TcpNetListener.MaxPendingAccepts, TimeSpan.FromSeconds(5)));
            Assert.Equal(TcpNetListener.MaxPendingAccepts, (int)queue.GetType().GetProperty("Count")!.GetValue(queue)!);
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }

}
