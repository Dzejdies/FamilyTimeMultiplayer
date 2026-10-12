using System;
using FamilyTimeMultiplayer.Core.Compatibility;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Replication;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class ReplicationCapabilityTests
{
    private static readonly EntityKind Rigid = new("rigid");
    private static readonly EntityStateDomain Charge = new("battery.charge");
    private static readonly AddonDescriptor Battery = new("battery", "1", "schema1", AddonRequirement.Optional);

    [Fact]
    public void OptionalStateAndActionsOnlyReachPeersWithMatchingCapabilities()
    {
        var capabilities = new ReplicationCapabilities(new[] { Battery });
        capabilities.RegisterState("battery", new EntityComponentAdapterDescriptor("battery.state", 1, Charge, new[] { Rigid }));
        capabilities.RegisterAction("battery", new GenericActionDescriptor("battery.recharge", 1, new[] { Rigid }));
        var state = new GenericEntityStateMessage(new NetworkEntityId(1, Rigid, 12), 1, Charge, 1, 1, new byte[] { 5 });
        var shared = AddonCapabilities.Shared(new[] { Battery }, new[] { Battery });
        Assert.True(capabilities.SupportsState(shared, state));
        Assert.True(capabilities.SupportsAction(shared, "battery.recharge"));
        Assert.False(capabilities.SupportsState(Array.Empty<AddonDescriptor>(), state));
        Assert.False(capabilities.SupportsAction(Array.Empty<AddonDescriptor>(), "battery.recharge"));
    }

    [Fact]
    public void ExtensionsCannotClaimAnotherExtensionsStateOrNativeActions()
    {
        var capabilities = new ReplicationCapabilities(new[] { Battery });
        Assert.Throws<ArgumentException>(() => capabilities.RegisterAction("battery", new GenericActionDescriptor("familytime.command", 1, Array.Empty<EntityKind>(), false)));
        Assert.Throws<ArgumentException>(() => capabilities.RegisterState("battery", new EntityComponentAdapterDescriptor("battery.state", 1, new EntityStateDomain("transform"), new[] { Rigid })));
        Assert.Throws<InvalidOperationException>(() => capabilities.RegisterAction("unknown", new GenericActionDescriptor("unknown.action", 1, new[] { Rigid })));
    }

    [Fact]
    public void NativeStateRemainsRequiredByTheMultiplayerProtocol()
    {
        var capabilities = new ReplicationCapabilities(Array.Empty<AddonDescriptor>());
        var state = new GenericEntityStateMessage(new NetworkEntityId(1, Rigid, 12), 1, new EntityStateDomain("native.status"), 1, 1, Array.Empty<byte>());
        Assert.True(capabilities.SupportsState(Array.Empty<AddonDescriptor>(), state));
        Assert.True(capabilities.SupportsAction(Array.Empty<AddonDescriptor>(), "familytime.command"));
    }
}
