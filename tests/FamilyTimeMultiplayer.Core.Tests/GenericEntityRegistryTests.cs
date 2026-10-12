using System;
using System.Collections.Generic;
using System.Threading;
using FamilyTimeMultiplayer.Core.Replication;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class GenericEntityRegistryTests
{
    private static readonly EntityKind AgentKind = new EntityKind("agent");
    private static readonly EntityKind RigidKind = new EntityKind("rigid");

    [Fact]
    public void IdentityIncludesWorldGenerationAndEntityKind()
    {
        var registry = new GenericEntityRegistry<string>(4);
        var agent = new NetworkEntityId(4, AgentKind, 12);
        var rigid = new NetworkEntityId(4, RigidKind, 12);

        registry.Register(agent, "agent-local", 1);
        registry.Register(rigid, "rigid-local", 1);

        Assert.Equal("agent-local", registry.GetLocal(agent));
        Assert.Equal("rigid-local", registry.GetLocal(rigid));
    }

    [Fact]
    public void RepeatedBindingIsIdempotentButConflictsFail()
    {
        var registry = new GenericEntityRegistry<string>(4);
        var identity = new NetworkEntityId(4, AgentKind, 12);

        Assert.Equal(EntityLifecycleResult.Applied, registry.Register(identity, "local-a", 1));
        Assert.Equal(EntityLifecycleResult.AlreadyApplied, registry.Register(identity, "local-a", 1));

        Assert.Throws<InvalidOperationException>(() => registry.Register(identity, "local-b", 1));
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new NetworkEntityId(4, AgentKind, 13), "local-a", 1));
    }

    [Fact]
    public void StaleLifecycleRevisionCannotRecreateOrRemoveEntity()
    {
        var registry = new GenericEntityRegistry<string>(4);
        var identity = new NetworkEntityId(4, AgentKind, 12);
        registry.Register(identity, "local-a", 3);
        registry.Unregister(identity, 4);

        Assert.Throws<InvalidOperationException>(() => registry.Register(identity, "local-b", 3));
        Assert.Throws<InvalidOperationException>(() => registry.Unregister(identity, 3));
        Assert.Equal(EntityLifecycleResult.Applied, registry.Register(identity, "local-b", 5));
    }

    [Fact]
    public void WorldGenerationChangeClearsBindingsAndRejectsOldIdentity()
    {
        var registry = new GenericEntityRegistry<string>(4);
        var oldIdentity = new NetworkEntityId(4, AgentKind, 12);
        registry.Register(oldIdentity, "local-a", 1);

        registry.ChangeWorldGeneration(5);

        Assert.Equal(0, registry.Count);
        Assert.Throws<InvalidOperationException>(() => registry.GetLocal(oldIdentity));
        Assert.Equal(EntityLifecycleResult.Applied,
            registry.Register(new NetworkEntityId(5, AgentKind, 12), "local-a", 1));
    }

    [Fact]
    public void OnlyOneOwnerCanControlAnEntityStateDomain()
    {
        var registry = new GenericEntityRegistry<string>(4);
        var identity = new NetworkEntityId(4, AgentKind, 12);
        registry.Register(identity, "local-a", 1);
        var transform = new EntityStateDomain("transform");

        registry.ClaimOwnership(identity, transform, new ReplicationOwner("agent-snapshots"));
        registry.ClaimOwnership(identity, transform, new ReplicationOwner("agent-snapshots"));
        registry.ClaimOwnership(identity, new EntityStateDomain("appearance"), new ReplicationOwner("visual-adapter"));

        Assert.Throws<InvalidOperationException>(() => registry.ClaimOwnership(
            identity, transform, new ReplicationOwner("component-adapter")));
    }

    [Fact]
    public void ComponentAdapterRegistrationRequiresUniqueVersionedContract()
    {
        var registry = new EntityComponentAdapterRegistry<string>();
        var adapter = new TestComponentAdapter("appearance", 1, AgentKind);

        Assert.Equal(EntityAdapterRegistrationResult.Registered, registry.Register(adapter));
        Assert.Equal(EntityAdapterRegistrationResult.AlreadyRegistered, registry.Register(adapter));
        Assert.Same(adapter, registry.Get("appearance"));
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new TestComponentAdapter("appearance", 2, AgentKind)));
    }

    [Fact]
    public void RegistryRejectsOperationsOutsideItsCreatingThread()
    {
        var registry = new GenericEntityRegistry<string>(4);
        var identity = new NetworkEntityId(4, AgentKind, 12);
        InvalidOperationException? observed = null;
        var worker = new Thread(() =>
        {
            try { registry.Register(identity, "local-a", 1); }
            catch (InvalidOperationException exception) { observed = exception; }
        });

        worker.Start();
        worker.Join();

        Assert.NotNull(observed);
        Assert.Contains("thread", observed.Message);
    }

    private sealed class TestComponentAdapter : IEntityComponentAdapter<string>
    {
        public TestComponentAdapter(string adapterId, int schemaVersion, EntityKind supportedKind)
        {
            Descriptor = new EntityComponentAdapterDescriptor(
                adapterId,
                schemaVersion,
                new EntityStateDomain("appearance"),
                new[] { supportedKind });
        }

        public EntityComponentAdapterDescriptor Descriptor { get; }
        public byte[] Capture(string entity) => Array.Empty<byte>();
        public bool TryValidate(ReadOnlyMemory<byte> state, out string error) { error = string.Empty; return true; }
        public void Apply(string entity, int schemaVersion, ReadOnlyMemory<byte> state) { }
    }
}
