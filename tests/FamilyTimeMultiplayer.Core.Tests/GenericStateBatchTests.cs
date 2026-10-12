using System;
using System.IO;
using System.Linq;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Replication;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class GenericStateBatchTests
{
    [Fact]
    public void DenseNativeStateIsSplitIntoBoundedTransportFrames()
    {
        var states = Enumerable.Range(0, 600).Select(index => State((ulong)index, new byte[] { 4, 3, 2, 1 })).ToArray();
        var batches = GenericEntityStateBatchMessage.CreateBatches(states).ToArray();
        Assert.True(batches.Length > 1);
        Assert.Equal(600, batches.Sum(batch => batch.States.Count));
        Assert.Equal(Enumerable.Range(0, 600).Select(index => (ulong)index), batches.SelectMany(batch => batch.States).Select(state => state.Identity.NetworkId));
        foreach (var batch in batches)
            Assert.Equal(batch.States.Count, Assert.IsType<GenericEntityStateBatchMessage>(MessageCodec.Decode(MessageCodec.Encode(batch))).States.Count);
    }

    [Fact]
    public void LargeComponentsAreSplitByPayloadSize()
    {
        var states = Enumerable.Range(0, 10).Select(index => State((ulong)index, new byte[256 * 1024]));
        var batches = GenericEntityStateBatchMessage.CreateBatches(states).ToArray();
        Assert.True(batches.Length >= 3);
        foreach (var batch in batches) Assert.True(MessageCodec.Encode(batch).Length < 2 * 1024 * 1024);
        Assert.Throws<InvalidDataException>(() => new GenericEntityStateBatchMessage(Enumerable.Range(0, 300).Select(index => State((ulong)index, new byte[1]))));
    }

    private static GenericEntityStateMessage State(ulong index, byte[] payload) =>
        new(new NetworkEntityId(1, new EntityKind("agent"), index), 1, new EntityStateDomain("appearance"), 1, 1, payload);
}
