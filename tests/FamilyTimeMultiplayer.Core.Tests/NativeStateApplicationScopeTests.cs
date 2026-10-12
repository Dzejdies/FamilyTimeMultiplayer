using System;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Replication;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class NativeStateApplicationScopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmediateAndDeferredStateUseNativeScopeAndRestoreIt(bool deferred)
    {
        bool nativeScope = false;
        bool appliedInScope = false;
        var registry = new GenericEntityRegistry<object>(1);
        var identity = new NetworkEntityId(1, new EntityKind("agent"), 7);
        var state = new GenericStateReplicationService<object>(registry, false, () =>
        {
            nativeScope = true;
            return new Lease(() => nativeScope = false);
        });
        state.RegisterAdapter(new AgentInfoComponentAdapter((_, _) => appliedInScope = nativeScope && state.IsApplyingRemoteState));
        if (!deferred) registry.Register(identity, (ushort)21, 1);
        state.Apply(new GenericEntityStateMessage(identity, 1, new EntityStateDomain("appearance"), 1, 1, new byte[4]));
        if (deferred) { registry.Register(identity, (ushort)21, 1); state.OnEntityAvailable(identity); }
        Assert.True(appliedInScope);
        Assert.False(nativeScope);
        Assert.False(state.IsApplyingRemoteState);
    }

    [Fact]
    public void ThrowingNativeAdapterRestoresScopeAndRejectsState()
    {
        bool nativeScope = false;
        var registry = new GenericEntityRegistry<object>(1);
        var identity = new NetworkEntityId(1, new EntityKind("agent"), 7);
        registry.Register(identity, (ushort)21, 1);
        var state = new GenericStateReplicationService<object>(registry, false, () =>
        {
            nativeScope = true;
            return new Lease(() => nativeScope = false);
        });
        state.RegisterAdapter(new AgentInfoComponentAdapter((_, _) => throw new InvalidOperationException("Native failure")));
        GenericStateApplyResult result = state.Apply(new GenericEntityStateMessage(identity, 1,
            new EntityStateDomain("appearance"), 1, 1, new byte[4]));
        Assert.Equal(GenericStateApplyStatus.Rejected, result.Status);
        Assert.False(nativeScope);
        Assert.False(state.IsApplyingRemoteState);
    }

    private sealed class Lease : IDisposable
    {
        private readonly Action _release;
        internal Lease(Action release) => _release = release;
        public void Dispose() => _release();
    }
}
