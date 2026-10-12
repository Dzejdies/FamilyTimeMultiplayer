using System.Collections.Generic;
using System;

namespace FamilyTimeMultiplayer.RuntimeHarness.Contracts;

public static class FamilyPersistencePreconditions
{
    public static IReadOnlyList<string> FindMissing(IReadOnlyDictionary<string, string> facts)
    {
        var missing = new List<string>();
        int familyCount = ReadInt(facts, "family.count", 0);
        if (familyCount < 2) missing.Add("two-registered-families");
        for (int index = 0; index < familyCount; index++)
        {
            if (ReadInt(facts, $"family.{index}.childCount", 0) == 0) missing.Add($"children-for-family-{index}");
            if (!ReadBool(facts, $"family.{index}.mamaAssigned")) missing.Add($"mama-for-family-{index}");
        }
        if (!ReadBool(facts, "family.childOwnershipUnique")) missing.Add("unique-child-ownership");
        if (ReadInt(facts, "family.totalOwnedChildren", 0) != ReadInt(facts, "family.liveChildCount", -1))
            missing.Add("all-live-children-owned");
        return missing;
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> facts, string key, int fallback) =>
        facts.TryGetValue(key, out string? value) && int.TryParse(value, out int parsed) ? parsed : fallback;

    private static bool ReadBool(IReadOnlyDictionary<string, string> facts, string key) =>
        facts.TryGetValue(key, out string? value) && bool.TryParse(value, out bool parsed) && parsed;
}
