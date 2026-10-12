using System;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Replication;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class EntityLifecycleSnapshotTests
{
    [Fact]
    public void ReloadedLocalBindingUsesHostLifecycleRevision()
    {
        var identity = new NetworkEntityId(4, new EntityKind("agent"), 7);
        var client = new GenericEntityRegistry<object>(4);
        client.Register(identity, (ushort)21, 1);
        client.Unregister(identity, 2);
        client.PrepareSnapshot(4, new[] { new EntityLifecycleBinding(identity, 5) });
        Assert.Equal(5ul, client.NextLifecycleRevision(identity));
        client.Register(identity, (ushort)31, client.NextLifecycleRevision(identity));
        Assert.Equal((ushort)31, client.GetLocal(identity));
        Assert.True(client.TryGetLifecycleRevision(identity, out ulong revision));
        Assert.Equal(5ul, revision);
        Assert.Throws<InvalidOperationException>(() => client.Register(identity, (ushort)32, 1));
    }

    [Fact]
    public void SnapshotMessageRoundTripsCanonicalHostRevisions()
    {
        var identity = new NetworkEntityId(4, new EntityKind("rigid"), 12);
        var map = new EntityLifecycleMapMessage(4, new[] { new EntityLifecycleBinding(identity, 3) });
        var decoded = Assert.IsType<EntityLifecycleMapMessage>(MessageCodec.Decode(MessageCodec.Encode(map)));
        Assert.Equal(identity, Assert.Single(decoded.Bindings).Identity);
        Assert.Equal(3ul, decoded.Bindings[0].Revision);
    }

    [Fact]
    public void InvalidSnapshotCannotEraseExistingBindings()
    {
        var identity = new NetworkEntityId(4, new EntityKind("agent"), 7);
        var registry = new GenericEntityRegistry<object>(4);
        registry.Register(identity, (ushort)21, 1);
        Assert.Throws<ArgumentException>(() => registry.PrepareSnapshot(4,
            new[] { new EntityLifecycleBinding(identity, 3), new EntityLifecycleBinding(identity, 5) }));
        Assert.Equal((ushort)21, registry.GetLocal(identity));
    }

    [Fact]
    public void DespawnBeforeCustomBindingDoesNotResurrectSnapshotEntity()
    {
        var identity = new NetworkEntityId(4, new EntityKind("custom.entity"), 7);
        var registry = new GenericEntityRegistry<object>(4);
        registry.PrepareSnapshot(4, new[] { new EntityLifecycleBinding(identity, 5) });
        registry.Unregister(identity, 6);
        Assert.Equal(7ul, registry.NextLifecycleRevision(identity));
        Assert.Throws<InvalidOperationException>(() => registry.Register(identity, new object(), 5));
    }
}
