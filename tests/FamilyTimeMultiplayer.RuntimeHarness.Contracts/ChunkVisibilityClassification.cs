namespace FamilyTimeMultiplayer.RuntimeHarness.Contracts;

public static class ChunkVisibilityClassification
{
    public static string Classify(bool chunkCullable, bool chunkVisible, int cachedInstanceCount)
    {
        if (!chunkCullable) return "not-chunk-cullable";
        string location = chunkVisible ? "inside-visible-chunks" : "outside-visible-chunks";
        return $"{location};cachedInstances={System.Math.Max(0, cachedInstanceCount)}";
    }
}
