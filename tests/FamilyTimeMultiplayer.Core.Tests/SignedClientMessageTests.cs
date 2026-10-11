using System;
using System.IO;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Security;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class SignedClientMessageTests
{
    [Fact]
    public void RelayedHandshakeProofCannotAuthorizeAttackersCommands()
    {
        using var owner = PlayerIdentity.Create();
        using var attacker = PlayerIdentity.Create();
        byte[] nonce = PlayerIdentity.CreateChallenge();
        var hello = new HelloMessage(ProtocolConstants.Version, "build", "Friend", owner.PublicKey);
        Assert.True(PlayerIdentity.VerifyHandshake(nonce, hello, owner.SignHandshake(nonce, hello)));
        using var verifier = new SignedClientVerifier(owner.PublicKey, nonce);
        byte[] payload = MessageCodec.Encode(new ClientReadyMessage(1));
        Assert.Throws<InvalidDataException>(() => verifier.Decode(new SignedClientMessage(1, payload, attacker.SignPacket(nonce, 1, payload))));
        Assert.IsType<ClientReadyMessage>(verifier.Decode(new SignedClientMessage(1, payload, owner.SignPacket(nonce, 1, payload))));
    }

    [Fact]
    public void SignedPacketsRejectReplayTamperingAndOtherConnections()
    {
        using var owner = PlayerIdentity.Create();
        byte[] nonce = PlayerIdentity.CreateChallenge();
        byte[] payload = MessageCodec.Encode(new ClientReadyMessage(1));
        var message = new SignedClientMessage(1, payload, owner.SignPacket(nonce, 1, payload));
        using var verifier = new SignedClientVerifier(owner.PublicKey, nonce);
        Assert.IsType<ClientReadyMessage>(verifier.Decode(message));
        Assert.Throws<InvalidDataException>(() => verifier.Decode(message));
        using var other = new SignedClientVerifier(owner.PublicKey, PlayerIdentity.CreateChallenge());
        Assert.Throws<InvalidDataException>(() => other.Decode(message));
        payload[1] = 2;
        using var tamper = new SignedClientVerifier(owner.PublicKey, nonce);
        Assert.Throws<InvalidDataException>(() => tamper.Decode(new SignedClientMessage(1, payload, message.Signature)));
    }

    [Fact]
    public void SignedTruncatedInnerMessageRemainsASenderProtocolFailure()
    {
        using var owner = PlayerIdentity.Create();
        byte[] nonce = PlayerIdentity.CreateChallenge();
        byte[] payload = { (byte)MessageType.Hello };
        using var verifier = new SignedClientVerifier(owner.PublicKey, nonce);
        Assert.Throws<InvalidDataException>(() => verifier.Decode(new SignedClientMessage(1, payload, owner.SignPacket(nonce, 1, payload))));
    }
}
