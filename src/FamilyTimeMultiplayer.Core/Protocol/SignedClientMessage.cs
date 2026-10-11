using System;
using System.IO;

namespace FamilyTimeMultiplayer.Core.Protocol;

public sealed class SignedClientMessage : INetMessage
{
    public const int MaxPayloadBytes = ProtocolConstants.MaxFrameBytes - 269;
    private readonly byte[] _payload;
    private readonly byte[] _signature;
    public SignedClientMessage(long sequence, byte[] payload, byte[] signature)
    {
        if (sequence < 1 || payload.Length < 1 || payload.Length > MaxPayloadBytes || signature.Length != 256) throw new InvalidDataException("Invalid signed client packet.");
        Sequence = sequence;
        _payload = (byte[])payload.Clone();
        _signature = (byte[])signature.Clone();
    }
    public long Sequence { get; }
    public byte[] Payload => (byte[])_payload.Clone();
    public byte[] Signature => (byte[])_signature.Clone();
    public MessageType Type => MessageType.SignedClient;
    public void Write(BinaryWriter writer) { writer.Write(Sequence); writer.Write(_payload.Length); writer.Write(_payload); writer.Write(_signature); }
    public static SignedClientMessage Read(BinaryReader reader)
    {
        long sequence = reader.ReadInt64();
        int count = ProtocolGuards.ReadCount(reader, MaxPayloadBytes, "signed client payload");
        if (count < 1 || count > reader.BaseStream.Length - reader.BaseStream.Position - 256) throw new InvalidDataException("Signed client payload exceeds its frame.");
        byte[] payload = reader.ReadBytes(count);
        if (payload.Length != count) throw new InvalidDataException("Truncated signed client payload.");
        return new SignedClientMessage(sequence, payload, reader.ReadBytes(256));
    }
}
