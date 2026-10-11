using System;
using System.IO;
using FamilyTimeMultiplayer.Core.Families;
using FamilyTimeMultiplayer.Core.Security;
using Xunit;
using FamilyTimeMultiplayer.Core.Protocol;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class FamilyIdentityTests
{
    [Fact]
    public void ProofRequiresTheOwnersPrivateKeyAndFreshChallenge()
    {
        using var owner = PlayerIdentity.Create();
        using var stranger = PlayerIdentity.Create();
        byte[] challenge = PlayerIdentity.CreateChallenge();
        var hello = new HelloMessage(ProtocolConstants.Version, "build", "Friend", owner.PublicKey);
        var strangerHello = new HelloMessage(hello.ProtocolVersion, hello.GameBuildId, hello.PlayerName, stranger.PublicKey);
        Assert.True(PlayerIdentity.VerifyHandshake(challenge, hello, owner.SignHandshake(challenge, hello)));
        Assert.False(PlayerIdentity.VerifyHandshake(challenge, hello, stranger.SignHandshake(challenge, strangerHello)));
        Assert.False(PlayerIdentity.VerifyHandshake(PlayerIdentity.CreateChallenge(), hello, owner.SignHandshake(challenge, hello)));
        using var saved = new MemoryStream();
        owner.Write(saved); saved.Position = 0;
        using var restored = PlayerIdentity.Read(saved);
        Assert.Equal(owner.PublicKey, restored.PublicKey);
        Assert.True(PlayerIdentity.VerifyHandshake(challenge, hello, restored.SignHandshake(challenge, hello)));
    }

    [Fact]
    public void HandshakeProofBindsTheRequestedNameAndBuild()
    {
        using var identity = PlayerIdentity.Create();
        byte[] nonce = PlayerIdentity.CreateChallenge();
        var hello = new HelloMessage(ProtocolConstants.Version, "build", "Friend", identity.PublicKey);
        byte[] proof = identity.SignHandshake(nonce, hello);
        Assert.True(PlayerIdentity.VerifyHandshake(nonce, hello, proof));
        Assert.False(PlayerIdentity.VerifyHandshake(nonce, new HelloMessage(hello.ProtocolVersion, "build", "Other", identity.PublicKey), proof));
        Assert.False(PlayerIdentity.VerifyHandshake(nonce, new HelloMessage(hello.ProtocolVersion, "other-build", "Friend", identity.PublicKey), proof));
    }

    [Fact]
    public void DisplayNameDoesNotGrantSavedFamilyOwnership()
    {
        using var owner = PlayerIdentity.Create();
        using var stranger = PlayerIdentity.Create();
        var record = new FamilyWorldRecord(new[] { new FamilyRecord("Friend", false, new ushort[] { 7 }, null, owner.PublicKey) });
        var registry = new FamilyIdentityRegistry(record, "Host");
        Assert.Null(registry.OwnerOf(stranger.PublicKey));
        Assert.False(registry.IsLegacy("Friend"));
        Assert.NotEqual("Friend", registry.CreateFamily(stranger.PublicKey, "Friend"));
        Assert.Equal("Friend", registry.OwnerOf(owner.PublicKey));
    }

    [Fact]
    public void LegacyFamilyRequiresExplicitHostBinding()
    {
        using var identity = PlayerIdentity.Create();
        var registry = new FamilyIdentityRegistry(new FamilyWorldRecord(new[] { new FamilyRecord("Friend", false, new ushort[] { 7 }, null) }), "Host");
        Assert.True(registry.IsLegacy("Friend"));
        Assert.Null(registry.OwnerOf(identity.PublicKey));
        registry.BindLegacy(identity.PublicKey, "Friend");
        Assert.Equal("Friend", registry.OwnerOf(identity.PublicKey));
        Assert.False(registry.IsLegacy("Friend"));
        Assert.Throws<InvalidOperationException>(() => registry.BindLegacy(identity.PublicKey, "Friend"));
        Assert.Throws<InvalidOperationException>(() => registry.BindLegacy(identity.PublicKey, "Host"));
    }

    [Fact]
    public void VersionOneRecordsRemainUnboundUntilHostMigration()
    {
        using var saved = new MemoryStream();
        using var writer = new BinaryWriter(saved, System.Text.Encoding.UTF8, true);
        writer.Write(0x464D5446u); writer.Write((byte)1); writer.Write(1);
        writer.Write("Friend"); writer.Write(false); writer.Write(1); writer.Write((ushort)7); writer.Write(false); writer.Write((ushort)0);
        saved.Position = 0;
        FamilyWorldRecord record = FamilyWorldRecord.Read(saved);
        Assert.Null(record.Families[0].PublicKey);
        Assert.True(new FamilyIdentityRegistry(record, "Host").IsLegacy("Friend"));
    }

    [Fact]
    public void NonCanonicalPublicIdentityIsRejected()
    {
        using var identity = PlayerIdentity.Create();
        Assert.Throws<InvalidDataException>(() => PlayerIdentity.ValidatePublicKey(" " + identity.PublicKey));
        var registry = new FamilyIdentityRegistry(FamilyWorldRecord.Empty, "Host");
        Assert.Throws<InvalidDataException>(() => registry.OwnerOf(string.Empty));
        Assert.NotEqual("Host", registry.CreateFamily(identity.PublicKey, "Host"));
    }

    [Fact]
    public void FamilyWorldRecordPersistsPublicIdentityOnly()
    {
        using var identity = PlayerIdentity.Create();
        var record = new FamilyWorldRecord(new[] { new FamilyRecord("Friend", false, new ushort[] { 7 }, 8, identity.PublicKey) });
        using var saved = new MemoryStream(); record.Write(saved); saved.Position = 0;
        FamilyRecord restored = FamilyWorldRecord.Read(saved).Families[0];
        Assert.Equal(identity.PublicKey, restored.PublicKey);
        Assert.Equal("Friend", new FamilyIdentityRegistry(new FamilyWorldRecord(new[] { restored }), "Host").OwnerOf(identity.PublicKey));
    }
}
