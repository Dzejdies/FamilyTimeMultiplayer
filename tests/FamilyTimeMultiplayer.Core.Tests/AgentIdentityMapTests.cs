using System;
using FamilyTimeMultiplayer.Core.Interpolation;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public class AgentIdentityMapTests
{
    [Fact]
    public void BindsBothDirections()
    {
        var map = new AgentIdentityMap();
        map.Bind(10, 20);
        Assert.True(map.TryGetLocal(10, out ushort local));
        Assert.True(map.TryGetHost(20, out ushort host));
        Assert.Equal((20, 10), (local, host));
    }

    [Fact]
    public void RebindingSamePairIsIdempotent()
    {
        var map = new AgentIdentityMap();
        map.Bind(1, 1);
        map.Bind(1, 1);
        Assert.Equal(1, map.Count);
    }

    [Fact]
    public void ConflictingBindThrows()
    {
        var map = new AgentIdentityMap();
        map.Bind(1, 5);
        Assert.Throws<InvalidOperationException>(() => map.Bind(1, 6));
        Assert.Throws<InvalidOperationException>(() => map.Bind(2, 5));
    }

    [Fact]
    public void UnbindRemovesBothDirections()
    {
        var map = new AgentIdentityMap();
        map.Bind(3, 4);
        Assert.True(map.UnbindHost(3, out ushort local));
        Assert.Equal(4, local);
        Assert.False(map.TryGetHost(4, out _));
    }
}
