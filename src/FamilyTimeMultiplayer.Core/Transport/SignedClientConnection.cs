using System;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Security;

namespace FamilyTimeMultiplayer.Core.Transport;

public sealed class SignedClientConnection : INetConnection
{
    private readonly INetConnection _connection;
    private readonly PlayerIdentity _identity;
    private readonly byte[] _nonce;
    private long _sequence;
    public SignedClientConnection(INetConnection connection, PlayerIdentity identity, byte[] nonce)
    {
        if (nonce.Length != 32) throw new ArgumentException("Invalid client channel nonce.", nameof(nonce));
        _connection = connection;
        _identity = identity;
        _nonce = (byte[])nonce.Clone();
    }
    public int Id => _connection.Id;
    public string RemoteDescription => _connection.RemoteDescription;
    public bool IsOpen => _connection.IsOpen;
    public string? CloseReason => _connection.CloseReason;
    public int PendingSendFrames => _connection.PendingSendFrames;
    public void Send(INetMessage message)
    {
        if (!IsOpen) return;
        byte[] payload = MessageCodec.Encode(message);
        long sequence = checked(++_sequence);
        _connection.Send(new SignedClientMessage(sequence, payload, _identity.SignPacket(_nonce, sequence, payload)));
    }
    public bool TryReceive(out INetMessage message) => _connection.TryReceive(out message!);
    public void Close(string reason) => _connection.Close(reason);
    public void Dispose() => _connection.Dispose();
}
