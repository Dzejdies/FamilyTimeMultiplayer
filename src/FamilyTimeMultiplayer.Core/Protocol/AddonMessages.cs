using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyTimeMultiplayer.Core.Protocol;

public enum AddonRequirement : byte
{
    Required = 0,
    Optional = 1,
}

public sealed class AddonDescriptor
{
    public AddonDescriptor(string id, string version, string contentHash, AddonRequirement requirement = AddonRequirement.Required)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || id.Any(character => !char.IsLetterOrDigit(character) && character != '.' && character != '-' && character != '_')) throw new InvalidDataException("Invalid addon identifier.");
        if (string.IsNullOrWhiteSpace(version) || version.Length > 32 || string.IsNullOrWhiteSpace(contentHash) || contentHash.Length > 128) throw new InvalidDataException("Invalid addon compatibility descriptor.");
        if (requirement != AddonRequirement.Required && requirement != AddonRequirement.Optional) throw new InvalidDataException("Invalid addon requirement.");
        Id = id; Version = version; ContentHash = contentHash; Requirement = requirement;
    }
    public string Id { get; }
    public string Version { get; }
    public string ContentHash { get; }
    public AddonRequirement Requirement { get; }
    public void Write(BinaryWriter writer) { writer.Write(Id); writer.Write(Version); writer.Write(ContentHash); writer.Write((byte)Requirement); }
    public static AddonDescriptor Read(BinaryReader reader) => new AddonDescriptor(ProtocolGuards.ReadText(reader, 64, "addon ID"), ProtocolGuards.ReadText(reader, 32, "addon version"), ProtocolGuards.ReadText(reader, 128, "addon content hash"), (AddonRequirement)reader.ReadByte());
}

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

public sealed class AddonMessage : INetMessage
{
    public const int MaximumPayloadBytes = 64 * 1024;
    private readonly byte[] _payload;
    public AddonMessage(string addonId, long worldGeneration, byte[] payload)
    {
        _ = new AddonDescriptor(addonId, "validated", "validated");
        if (worldGeneration < 1 || payload == null || payload.Length > MaximumPayloadBytes) throw new InvalidDataException("Invalid addon payload or world generation.");
        AddonId = addonId; WorldGeneration = worldGeneration; _payload = (byte[])payload.Clone();
    }
    public string AddonId { get; }
    public long WorldGeneration { get; }
    public byte[] Payload => (byte[])_payload.Clone();
    public MessageType Type => MessageType.Addon;
    public void Write(BinaryWriter writer) { writer.Write(AddonId); writer.Write(WorldGeneration); writer.Write(_payload.Length); writer.Write(_payload); }
    public static AddonMessage Read(BinaryReader reader)
    {
        string id = ProtocolGuards.ReadText(reader, 64, "addon ID");
        long generation = reader.ReadInt64();
        int length = reader.ReadInt32();
        if (length < 0 || length > MaximumPayloadBytes || length > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("Invalid addon payload length.");
        return new AddonMessage(id, generation, reader.ReadBytes(length));
    }
}
