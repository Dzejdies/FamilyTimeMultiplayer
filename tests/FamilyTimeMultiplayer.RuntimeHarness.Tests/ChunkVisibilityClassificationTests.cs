using FamilyTimeMultiplayer.RuntimeHarness.Contracts;
using Xunit;

namespace FamilyTimeMultiplayer.RuntimeHarness.Tests;

public sealed class ChunkVisibilityClassificationTests
{
    [Fact]
    public void Classify_reports_cullable_entity_outside_visible_chunks_with_cached_chunk_data()
    {
        string classification = ChunkVisibilityClassification.Classify(
            chunkCullable: true,
            chunkVisible: false,
            cachedInstanceCount: 3);

        Assert.Equal("outside-visible-chunks;cachedInstances=3", classification);
    }

    [Fact]
    public void Classify_reports_non_cullable_templates_explicitly()
    {
        string classification = ChunkVisibilityClassification.Classify(
            chunkCullable: false,
            chunkVisible: false,
            cachedInstanceCount: 0);

        Assert.Equal("not-chunk-cullable", classification);
    }

    [Fact]
    public void Classify_reports_visible_chunk_without_cache()
    {
        string classification = ChunkVisibilityClassification.Classify(
            chunkCullable: true,
            chunkVisible: true,
            cachedInstanceCount: 0);

        Assert.Equal("inside-visible-chunks;cachedInstances=0", classification);
    }
}
