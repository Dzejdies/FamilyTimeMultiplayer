using System;
using System.IO;

namespace FamilyTimeMultiplayer.Core.Protocol;

public sealed class IdentityChallengeMessage : INetMessage
{
    private readonly byte[] _challenge;
    public IdentityChallengeMessage(byte[] challenge)
    {
        if (challenge.Length != 32) throw new InvalidDataException("Invalid identity challenge size.");
        _challenge = (byte[])challenge.Clone();
    }
    public byte[] Challenge => (byte[])_challenge.Clone();
    public MessageType Type => MessageType.IdentityChallenge;
    public void Write(BinaryWriter writer) => writer.Write(_challenge);
    public static IdentityChallengeMessage Read(BinaryReader reader) => new IdentityChallengeMessage(reader.ReadBytes(32));
}

public sealed class IdentityProofMessage : INetMessage
{
    private readonly byte[] _signature;
    public IdentityProofMessage(byte[] signature)
    {
        if (signature.Length != 256) throw new InvalidDataException("Invalid identity signature size.");
        _signature = (byte[])signature.Clone();
    }
    public byte[] Signature => (byte[])_signature.Clone();
    public MessageType Type => MessageType.IdentityProof;
    public void Write(BinaryWriter writer) => writer.Write(_signature);
    public static IdentityProofMessage Read(BinaryReader reader) => new IdentityProofMessage(reader.ReadBytes(256));
}

public sealed class FamilyApprovalPendingMessage : INetMessage
{
    public MessageType Type => MessageType.FamilyApprovalPending;
    public void Write(BinaryWriter writer) { }
    public static FamilyApprovalPendingMessage Read(BinaryReader reader) => new FamilyApprovalPendingMessage();
}
