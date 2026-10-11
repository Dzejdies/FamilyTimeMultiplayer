using System;
using System.IO;
using System.Security.Cryptography;
using FamilyTimeMultiplayer.Core.Protocol;

namespace FamilyTimeMultiplayer.Core.Security;

public sealed class SignedClientVerifier : IDisposable
{
    private readonly RSA _key;
    private readonly byte[] _nonce;
    private long _next = 1;
    public SignedClientVerifier(string publicKey, byte[] nonce)
    {
        if (nonce.Length != 32) throw new InvalidDataException("Invalid signed channel nonce.");
        _nonce = (byte[])nonce.Clone();
        _key = RSA.Create();
        _key.ImportParameters(PlayerIdentity.ParsePublicKey(publicKey));
    }
    public INetMessage Decode(SignedClientMessage message)
    {
        if (message.Sequence != _next) throw new InvalidDataException("Signed client packet is replayed or out of order.");
        byte[] payload = message.Payload;
        bool valid;
        try { valid = _key.VerifyData(PlayerIdentity.PacketPayload(_nonce, message.Sequence, payload), message.Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); }
        catch (CryptographicException) { valid = false; }
        if (!valid) throw new InvalidDataException("Client packet signature failed.");
        INetMessage decoded;
        try { decoded = MessageCodec.Decode(payload); }
        catch (EndOfStreamException exception) { throw new InvalidDataException("Truncated authenticated client message.", exception); }
        if (decoded is SignedClientMessage) throw new InvalidDataException("Nested client signatures are not supported.");
        _next = checked(_next + 1);
        return decoded;
    }
    public void Dispose() => _key.Dispose();
}
