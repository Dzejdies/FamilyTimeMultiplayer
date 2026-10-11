using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyTimeMultiplayer.Core.Protocol;

public sealed class AddonDescriptor
{
    public AddonDescriptor(string id, string version, string contentHash)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || id.Any(character => !char.IsLetterOrDigit(character) && character != '.' && character != '-' && character != '_')) throw new InvalidDataException("Invalid addon identifier.");
        if (string.IsNullOrWhiteSpace(version) || version.Length > 32 || string.IsNullOrWhiteSpace(contentHash) || contentHash.Length > 128) throw new InvalidDataException("Invalid addon compatibility descriptor.");
        Id = id; Version = version; ContentHash = contentHash;
    }
    public string Id { get; }
    public string Version { get; }
    public string ContentHash { get; }
    public void Write(BinaryWriter writer) { writer.Write(Id); writer.Write(Version); writer.Write(ContentHash); }
    public static AddonDescriptor Read(BinaryReader reader) => new AddonDescriptor(ProtocolGuards.ReadText(reader, 64, "addon ID"), ProtocolGuards.ReadText(reader, 32, "addon version"), ProtocolGuards.ReadText(reader, 128, "addon content hash"));
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
        return left.Count == right.Count && left.Zip(right, (a, b) => a.Id == b.Id && a.Version == b.Version && a.ContentHash == b.ContentHash).All(match => match);
    }
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
