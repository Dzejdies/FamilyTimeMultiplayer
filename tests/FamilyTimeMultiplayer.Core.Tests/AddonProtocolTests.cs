using System;
using System.IO;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Security;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class AddonProtocolTests
{
    [Fact]
    public void AddonMessageRoundTripsAndCopiesPayload()
    {
        byte[] source = { 1, 2, 3 };
        var message = new AddonMessage("aircraft", 7, source);
        source[0] = 9;
        var decoded = Assert.IsType<AddonMessage>(MessageCodec.Decode(MessageCodec.Encode(message)));
        Assert.Equal("aircraft", decoded.AddonId);
        Assert.Equal(7, decoded.WorldGeneration);
        Assert.Equal(new byte[] { 1, 2, 3 }, decoded.Payload);
        decoded.Payload[0] = 9;
        Assert.Equal(1, decoded.Payload[0]);
    }

    [Fact]
    public void OversizedAndTruncatedPayloadsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => new AddonMessage("aircraft", 1, new byte[65537]));
        byte[] encoded = MessageCodec.Encode(new AddonMessage("aircraft", 1, new byte[5]));
        Array.Resize(ref encoded, encoded.Length - 1);
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(encoded));
    }

    [Fact]
    public void CapabilityChangesInvalidateIdentityProof()
    {
        using var identity = PlayerIdentity.Create();
        byte[] challenge = PlayerIdentity.CreateChallenge();
        var hello = new HelloMessage(20, "build", "player", identity.PublicKey, new[] { new AddonDescriptor("aircraft", "1", "hash") });
        byte[] proof = identity.SignHandshake(challenge, hello);
        var altered = new HelloMessage(20, "build", "player", identity.PublicKey, Array.Empty<AddonDescriptor>());
        Assert.False(PlayerIdentity.VerifyHandshake(challenge, altered, proof));
        Assert.True(PlayerIdentity.VerifyHandshake(challenge, Assert.IsType<HelloMessage>(MessageCodec.Decode(MessageCodec.Encode(hello))), proof));
    }

    [Fact]
    public void WelcomeCarriesCanonicalAddonGenerationIndependentOfTransferIdentity()
    {
        var welcome = new WelcomeMessage(1, 2, 100, FamilyMode.Shared, 9, 4);
        var decoded = Assert.IsType<WelcomeMessage>(MessageCodec.Decode(MessageCodec.Encode(welcome)));
        Assert.Equal(9, decoded.TransferId);
        Assert.Equal(4, decoded.WorldGeneration);
    }

    [Fact]
    public void SignedAddonPacketsRejectPayloadTamperingAndReplay()
    {
        using var identity = PlayerIdentity.Create();
        byte[] nonce = PlayerIdentity.CreateChallenge();
        byte[] payload = MessageCodec.Encode(new AddonMessage("aircraft", 2, new byte[] { 7 }));
        var signed = new SignedClientMessage(1, payload, identity.SignPacket(nonce, 1, payload));
        using var verifier = new SignedClientVerifier(identity.PublicKey, nonce);
        Assert.IsType<AddonMessage>(verifier.Decode(signed));
        Assert.Throws<InvalidDataException>(() => verifier.Decode(signed));
        payload[payload.Length - 1] = 8;
        using var untouched = new SignedClientVerifier(identity.PublicKey, nonce);
        Assert.Throws<InvalidDataException>(() => untouched.Decode(new SignedClientMessage(1, payload, signed.Signature)));
    }

    [Fact]
    public void CompatibilityRequiresExactCanonicalSets()
    {
        var first = new AddonDescriptor("aircraft", "1", "hash");
        Assert.True(AddonCapabilities.AreCompatible(new[] { first }, new[] { first }));
        Assert.False(AddonCapabilities.AreCompatible(new[] { first }, Array.Empty<AddonDescriptor>()));
        Assert.False(AddonCapabilities.AreCompatible(new[] { first }, new[] { new AddonDescriptor("aircraft", "2", "hash") }));
        Assert.Throws<InvalidDataException>(() => AddonCapabilities.Normalize(new[] { first, first }));
    }
}
