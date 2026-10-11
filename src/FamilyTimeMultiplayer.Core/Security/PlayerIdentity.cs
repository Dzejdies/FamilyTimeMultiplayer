using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FamilyTimeMultiplayer.Core.Protocol;

namespace FamilyTimeMultiplayer.Core.Security;

public sealed class PlayerIdentity : IDisposable
{
    private readonly RSA _key;
    private PlayerIdentity(RSA key)
    {
        _key = key;
        RSAParameters parameters = key.ExportParameters(false);
        PublicKey = Convert.ToBase64String(parameters.Modulus!) + "." + Convert.ToBase64String(parameters.Exponent!);
        ValidatePublicKey(PublicKey);
    }
    public string PublicKey { get; }
    public static string Fingerprint(string publicKey)
    {
        ValidatePublicKey(publicKey);
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(publicKey))).Replace("-", string.Empty).Substring(0, 16);
    }
    public static PlayerIdentity Create() { RSA key = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false }; return new PlayerIdentity(key); }
    public static byte[] CreateChallenge() { var bytes = new byte[32]; using var random = RandomNumberGenerator.Create(); random.GetBytes(bytes); return bytes; }
    private static byte[] HandshakePayload(byte[] nonce, HelloMessage hello)
    {
        if (nonce.Length != 32) throw new InvalidDataException("Invalid identity challenge.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.UTF8.GetBytes("FTMP family handshake v2\0"));
        writer.Write(nonce); writer.Write(MessageCodec.Encode(hello));
        return stream.ToArray();
    }
    public byte[] SignHandshake(byte[] nonce, HelloMessage hello)
    {
        if (hello.PublicKey != PublicKey) throw new InvalidDataException("Handshake does not identify the signing player.");
        return _key.SignData(HandshakePayload(nonce, hello), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
    public static bool VerifyHandshake(byte[] nonce, HelloMessage hello, byte[] signature)
    {
        if (signature.Length != 256) return false;
        using RSA key = RSA.Create();
        RSAParameters parameters = ParsePublicKey(hello.PublicKey);
        try { key.ImportParameters(parameters); return key.VerifyData(HandshakePayload(nonce, hello), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); }
        catch (CryptographicException) { return false; }
    }
    public byte[] SignPacket(byte[] nonce, long sequence, byte[] payload) => _key.SignData(PacketPayload(nonce, sequence, payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    internal static byte[] PacketPayload(byte[] nonce, long sequence, byte[] payload)
    {
        if (nonce.Length != 32 || sequence < 1) throw new InvalidDataException("Invalid signed client channel.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.UTF8.GetBytes("FTMP signed client packet v1\0"));
        writer.Write(nonce); writer.Write(sequence); writer.Write(payload);
        return stream.ToArray();
    }
    public static void ValidatePublicKey(string publicKey) => ParsePublicKey(publicKey);
    internal static RSAParameters ParsePublicKey(string publicKey)
    {
        try
        {
            if (publicKey.Length > 512) throw new InvalidDataException("Identity public key is too long.");
            string[] pieces = publicKey.Split('.');
            if (pieces.Length != 2) throw new InvalidDataException("Invalid identity public key.");
            byte[] modulus = Convert.FromBase64String(pieces[0]);
            byte[] exponent = Convert.FromBase64String(pieces[1]);
            if (modulus.Length != 256 || (modulus[0] & 128) == 0 || (modulus[255] & 1) == 0 || pieces[1] != "AQAB" || Convert.ToBase64String(modulus) != pieces[0]) throw new InvalidDataException($"Identity requires a canonical RSA2048 public key (modulus bytes={modulus.Length}, exponent={pieces[1]}).");
            return new RSAParameters { Modulus = modulus, Exponent = exponent };
        }
        catch (FormatException exception) { throw new InvalidDataException("Invalid identity key encoding.", exception); }
    }
    public void Write(Stream stream)
    {
        RSAParameters p = _key.ExportParameters(true);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(0x31495446u);
        foreach (byte[]? bytes in new[] { p.Modulus, p.Exponent, p.D, p.P, p.Q, p.DP, p.DQ, p.InverseQ })
        {
            if (bytes == null) throw new InvalidOperationException("Missing private key parameter.");
            writer.Write(bytes.Length); writer.Write(bytes);
        }
    }
    public static PlayerIdentity Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        if (reader.ReadUInt32() != 0x31495446u) throw new InvalidDataException("Invalid local player key file.");
        var values = new byte[8][];
        for (int i = 0; i < values.Length; i++)
        {
            int length = reader.ReadInt32();
            if (length < 1 || length > 512) throw new InvalidDataException("Invalid local player key parameter.");
            values[i] = reader.ReadBytes(length);
            if (values[i].Length != length) throw new InvalidDataException("Truncated local player key.");
        }
        if (stream.Position != stream.Length) throw new InvalidDataException("Trailing local player key data.");
        RSA key = RSA.Create();
        try
        {
            key.ImportParameters(new RSAParameters { Modulus = values[0], Exponent = values[1], D = values[2], P = values[3], Q = values[4], DP = values[5], DQ = values[6], InverseQ = values[7] });
            return new PlayerIdentity(key);
        }
        catch { key.Dispose(); throw; }
    }
    public void Dispose() => _key.Dispose();
}
