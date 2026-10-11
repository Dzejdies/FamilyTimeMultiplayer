using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Transport;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class TransferAdmissionSecurityTests
{
    [Theory]
    [InlineData(1073741825L)]
    [InlineData(long.MaxValue)]
    public void OversizedWorldIsRejectedBeforeTransfer(long length)
    {
        var welcome = new WelcomeMessage(1, 3, length, FamilyMode.Separate);
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(MessageCodec.Encode(welcome)));
    }

    [Fact]
    public void TransferIdentitySurvivesCodecAndCannotBeZero()
    {
        var ready = (ClientReadyMessage)MessageCodec.Decode(MessageCodec.Encode(new ClientReadyMessage(17)));
        var welcome = (WelcomeMessage)MessageCodec.Decode(MessageCodec.Encode(new WelcomeMessage(1, 3, 10, FamilyMode.Shared, 17)));
        Assert.Equal(17, ready.TransferId);
        Assert.Equal(17, welcome.TransferId);
        Assert.Throws<InvalidDataException>(() => new ClientReadyMessage(0));
    }

    [Fact]
    public void BoundedHandshakeTextPreservesUnicodePlayerNames()
    {
        var hello = new HelloMessage(ProtocolConstants.Version, "build", "Zażółć 🐺");
        Assert.Equal(hello.PlayerName, ((HelloMessage)MessageCodec.Decode(MessageCodec.Encode(hello))).PlayerName);
    }

    [Fact]
    public void HandshakeTextLengthIsCheckedBeforeDecodingTheString()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((byte)MessageType.Hello); writer.Write(ProtocolConstants.Version);
        writer.Write((byte)0x80); writer.Write((byte)0x80); writer.Write((byte)0x80); writer.Write((byte)0x04);
        writer.Write(new byte[128]);
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(stream.ToArray()));
    }

    [Fact]
    public void PreHelloWorldPacketsAreRejectedBeforeTheirParserRuns()
    {
        using var listener = new TcpNetListener(IPAddress.Loopback, 0);
        using var client = new TcpClient(); client.Connect(IPAddress.Loopback, listener.Port);
        INetConnection? receiver = null;
        Assert.True(SpinWait.SpinUntil(() => listener.TryAccept(out receiver!), TimeSpan.FromSeconds(5)));
        using (receiver!)
        {
            using var writer = new BinaryWriter(client.GetStream());
            byte[] payload = { (byte)MessageType.AgentSnapshot };
            writer.Write(payload.Length); writer.Write(payload);
            Assert.True(SpinWait.SpinUntil(() => !receiver!.IsOpen, TimeSpan.FromSeconds(5)));
            Assert.Contains("handshake message required", receiver!.CloseReason);
        }
    }

    [Fact]
    public void PreHelloFrameIsRejectedBeforeItsPayloadIsRead()
    {
        using var listener = new TcpNetListener(IPAddress.Loopback, 0);
        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, listener.Port);
        INetConnection? receiver = null;
        Assert.True(SpinWait.SpinUntil(() => listener.TryAccept(out receiver!), TimeSpan.FromSeconds(5)));
        using (receiver!)
        {
            client.GetStream().Write(BitConverter.GetBytes(4097));
            Assert.True(SpinWait.SpinUntil(() => !receiver!.IsOpen, TimeSpan.FromSeconds(5)));
            Assert.Contains("frame length 4097", receiver!.CloseReason);
        }
    }

    [Fact]
    public void AdmissionPromotesASmallInboxForLegitimateFrames()
    {
        using var listener = new TcpNetListener(IPAddress.Loopback, 0);
        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, listener.Port);
        INetConnection? receiver = null;
        Assert.True(SpinWait.SpinUntil(() => listener.TryAccept(out receiver!), TimeSpan.FromSeconds(5)));
        using (receiver!)
        {
            using var writer = new BinaryWriter(client.GetStream());
            byte[] hello = MessageCodec.Encode(new HelloMessage(1, "b", "peer"));
            for (int i = 0; i < 17; i++) { writer.Write(hello.Length); writer.Write(hello); }
            object queue = typeof(TcpNetConnection).GetField("_incoming", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(receiver)!;
            Assert.True(SpinWait.SpinUntil(() => (int)queue.GetType().GetProperty("Count")!.GetValue(queue)! == 16, TimeSpan.FromSeconds(5)));
            ((TcpNetConnection)receiver!).Admit();
            Assert.True(SpinWait.SpinUntil(() => (int)queue.GetType().GetProperty("Count")!.GetValue(queue)! == 17, TimeSpan.FromSeconds(5)));
            byte[] bytes = MessageCodec.Encode(new SignedClientMessage(1, MessageCodec.Encode(new WorldChunkMessage(0, new byte[ProtocolConstants.WorldChunkBytes])), new byte[256]));
            writer.Write(bytes.Length); writer.Write(bytes);
            bool found = false;
            Assert.True(SpinWait.SpinUntil(() => { while (receiver.TryReceive(out INetMessage message)) if (message is SignedClientMessage) found = true; return found; }, TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public void PendingRelayAcceptAlsoRetainsOnlyASocket()
    {
        using var relay = new FamilyTimeMultiplayer.Core.Transport.Relay.RelayServer(IPAddress.Loopback, 0, _ => { });
        using var host = new FamilyTimeMultiplayer.Core.Transport.Relay.RelayHostListener("127.0.0.1", relay.Port);
        Assert.True(SpinWait.SpinUntil(() => host.State == FamilyTimeMultiplayer.Core.Transport.Relay.RelayHostState.Ready, TimeSpan.FromSeconds(5)));
        PendingConnect pending = PendingConnect.StartViaRelay("127.0.0.1", relay.Port, host.Code!, TimeSpan.FromSeconds(5));
        Assert.True(SpinWait.SpinUntil(() => pending.IsCompleted, TimeSpan.FromSeconds(5)));
        using INetConnection client = pending.Connection!;
        var queue = (IEnumerable)typeof(FamilyTimeMultiplayer.Core.Transport.Relay.RelayHostListener).GetField("_accepted", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        object? socket = null;
        Assert.True(SpinWait.SpinUntil(() => { foreach (object item in queue) { socket = item; return true; } return false; }, TimeSpan.FromSeconds(5)));
        Assert.IsType<TcpClient>(socket);
    }

    [Fact]
    public void PendingAcceptRetainsOnlyASocketWithoutProtocolReader()
    {
        using var listener = new TcpNetListener(IPAddress.Loopback, 0);
        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, listener.Port);
        var queue = (IEnumerable)typeof(TcpNetListener).GetField("_accepted", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(listener)!;
        object? pending = null;
        Assert.True(SpinWait.SpinUntil(() => { foreach (object item in queue) { pending = item; return true; } return false; }, TimeSpan.FromSeconds(5)));
        Assert.IsType<TcpClient>(pending);
    }
}
