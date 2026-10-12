using System;
using System.IO;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Security;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class AddonNegotiationTests
{
    [Fact]
    public void DifferentOptionalAddonsDoNotPreventJoining()
    {
        var host = new[] { Optional("host.effects") };
        var client = new[] { Optional("client.hud") };
        Assert.True(AddonCapabilities.AreCompatible(host, client));
        Assert.Empty(AddonCapabilities.Shared(host, client));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RequiredAddonMustExistOnBothPeers(bool requiredOnHost)
    {
        var required = new[] { new AddonDescriptor("aircraft", "1", "content") };
        var absent = Array.Empty<AddonDescriptor>();
        Assert.False(AddonCapabilities.AreCompatible(requiredOnHost ? required : absent, requiredOnHost ? absent : required));
    }

    [Fact]
    public void OnlyMatchingOptionalCapabilitiesCanExchangeMessages()
    {
        var host = new[] { Optional("shared"), Optional("different", "1") };
        var client = new[] { Optional("shared"), Optional("different", "2") };
        Assert.True(AddonCapabilities.AreCompatible(host, client));
        Assert.Equal("shared", Assert.Single(AddonCapabilities.Shared(host, client)).Id);
    }

    [Fact]
    public void RequiredAndOptionalDeclarationsStillRequireMatchingContent()
    {
        var host = new[] { new AddonDescriptor("aircraft", "1", "content") };
        Assert.True(AddonCapabilities.AreCompatible(host, new[] { Optional("aircraft") }));
        Assert.False(AddonCapabilities.AreCompatible(host, new[] { Optional("aircraft", "2") }));
        Assert.Throws<InvalidDataException>(() => AddonCapabilities.Shared(host, Array.Empty<AddonDescriptor>()));
    }

    [Fact]
    public void WelcomeExposesHostCapabilitiesToClient()
    {
        var welcome = new WelcomeMessage(1, 2, 100, FamilyMode.Separate, addons: new[] { Optional("shared") });
        var decoded = Assert.IsType<WelcomeMessage>(MessageCodec.Decode(MessageCodec.Encode(welcome)));
        Assert.Equal(AddonRequirement.Optional, Assert.Single(decoded.Addons).Requirement);
    }

    [Fact]
    public void CompatibilityRequirementIsCoveredByIdentitySignature()
    {
        using var identity = PlayerIdentity.Create();
        byte[] challenge = PlayerIdentity.CreateChallenge();
        var original = new HelloMessage(ProtocolConstants.Version, "build", "player", identity.PublicKey, new[] { Optional("shared") });
        byte[] proof = identity.SignHandshake(challenge, original);
        var altered = new HelloMessage(ProtocolConstants.Version, "build", "player", identity.PublicKey, new[] { new AddonDescriptor("shared", "1", "content") });
        Assert.False(PlayerIdentity.VerifyHandshake(challenge, altered, proof));
        var decoded = Assert.IsType<HelloMessage>(MessageCodec.Decode(MessageCodec.Encode(original)));
        Assert.True(PlayerIdentity.VerifyHandshake(challenge, decoded, proof));
    }

    private static AddonDescriptor Optional(string id, string version = "1") =>
        new(id, version, "content", AddonRequirement.Optional);
}
