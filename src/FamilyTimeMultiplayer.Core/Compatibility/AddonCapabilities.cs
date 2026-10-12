using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyTimeMultiplayer.Core.Protocol;

public static class AddonCapabilities
{
    public const int MaximumAddons = 8;
    public static IReadOnlyList<AddonDescriptor> Normalize(IEnumerable<AddonDescriptor> descriptors)
    {
        AddonDescriptor[] result = descriptors.Take(MaximumAddons + 1).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (result.Length > MaximumAddons || result.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != result.Length) throw new InvalidDataException("Too many or duplicate addons.");
        return Array.AsReadOnly(result);
    }
    public static bool AreCompatible(IEnumerable<AddonDescriptor> first, IEnumerable<AddonDescriptor> second)
    {
        IReadOnlyList<AddonDescriptor> left = Normalize(first), right = Normalize(second);
        return RequiredMatch(left, right) && RequiredMatch(right, left);
    }

    public static IReadOnlyList<AddonDescriptor> Shared(IEnumerable<AddonDescriptor> first, IEnumerable<AddonDescriptor> second)
    {
        IReadOnlyList<AddonDescriptor> left = Normalize(first), right = Normalize(second);
        if (!AreCompatible(left, right)) throw new InvalidDataException("Required multiplayer addon is missing or incompatible.");
        return Array.AsReadOnly(left.Where(addon => right.Any(candidate => Matches(addon, candidate))).ToArray());
    }

    private static bool RequiredMatch(IReadOnlyList<AddonDescriptor> required, IReadOnlyList<AddonDescriptor> available) =>
        required.Where(addon => addon.Requirement == AddonRequirement.Required)
            .All(addon => available.Any(candidate => Matches(addon, candidate)));

    private static bool Matches(AddonDescriptor first, AddonDescriptor second) =>
        first.Id == second.Id && first.Version == second.Version && first.ContentHash == second.ContentHash;

    public static IReadOnlyList<AddonDescriptor> Read(BinaryReader reader)
    {
        int count = reader.ReadByte();
        if (count > MaximumAddons) throw new InvalidDataException("Too many addons.");
        var values = new AddonDescriptor[count];
        for (int index = 0; index < count; index++) values[index] = AddonDescriptor.Read(reader);
        return Normalize(values);
    }
}

