using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyTimeMultiplayer.Core.Families;

public sealed class FamilyRecord
{
    public FamilyRecord(string owner, bool isHostFamily, IReadOnlyList<ushort> members, ushort? mamaAgent, string? publicKey = null)
    {
        if (string.IsNullOrEmpty(owner))
        {
            throw new ArgumentException("A family record needs an owner.", nameof(owner));
        }
        if (publicKey != null) Security.PlayerIdentity.ValidatePublicKey(publicKey);
        PublicKey = publicKey;
        Owner = owner;
        IsHostFamily = isHostFamily;
        Members = members ?? throw new ArgumentNullException(nameof(members));
        MamaAgent = mamaAgent;
    }

    public string? PublicKey { get; }
    public string Owner { get; }
    public bool IsHostFamily { get; }
    public IReadOnlyList<ushort> Members { get; }
    public ushort? MamaAgent { get; }
}

public sealed class FamilyWorldRecord
{
    public const string SectionId = "ftmp.families";
    private const uint Magic = 0x464D5446;
    private const byte FormatVersion = 2;
    private const int MaxFamilies = 1024;
    private const int MaxMembers = ushort.MaxValue;

    public FamilyWorldRecord(IReadOnlyList<FamilyRecord> families)
    {
        if (families != null && families.Where(f => !f.IsHostFamily && f.PublicKey != null).GroupBy(f => f.PublicKey, StringComparer.Ordinal).Any(group => group.Count() > 1)) throw new ArgumentException("A player identity may own only one family.", nameof(families));
        Families = families ?? throw new ArgumentNullException(nameof(families));
        if (families.Count(f => f.IsHostFamily) > 1)
        {
            throw new ArgumentException("Only one family can belong to the host.", nameof(families));
        }
        if (families.Select(f => f.Owner).Distinct(StringComparer.Ordinal).Count() != families.Count)
        {
            throw new ArgumentException("Family owners must be unique.", nameof(families));
        }
    }

    public static FamilyWorldRecord Empty { get; } = new FamilyWorldRecord(Array.Empty<FamilyRecord>());

    public IReadOnlyList<FamilyRecord> Families { get; }

    public bool IsSeparateWorld => Families.Count > 0;

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(Families.Count);
        foreach (FamilyRecord family in Families)
        {
            writer.Write(family.Owner);
            writer.Write(family.IsHostFamily);
            writer.Write(family.Members.Count);
            foreach (ushort member in family.Members)
            {
                writer.Write(member);
            }
            writer.Write(family.MamaAgent.HasValue);
            writer.Write(family.MamaAgent ?? 0);
            writer.Write(family.PublicKey != null);
            if (family.PublicKey != null) writer.Write(family.PublicKey);
        }
    }

    public static FamilyWorldRecord Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException($"Section {SectionId} has a wrong magic number.");
        }
        byte version = reader.ReadByte();
        if (version != 1 && version != FormatVersion)
        {
            throw new InvalidDataException($"Section {SectionId} has unsupported version {version}.");
        }
        int familyCount = ReadCount(reader, MaxFamilies, "family");
        var families = new FamilyRecord[familyCount];
        for (int i = 0; i < familyCount; i++)
        {
            string owner = Protocol.ProtocolGuards.ReadText(reader, 128, "family owner");
            bool isHost = reader.ReadBoolean();
            int memberCount = ReadCount(reader, MaxMembers, "member");
            var members = new ushort[memberCount];
            for (int m = 0; m < memberCount; m++)
            {
                members[m] = reader.ReadUInt16();
            }
            bool hasMama = reader.ReadBoolean();
            ushort mama = reader.ReadUInt16();
            string? key = version >= 2 && reader.ReadBoolean() ? Protocol.ProtocolGuards.ReadText(reader, 512, "family identity") : null;
            families[i] = new FamilyRecord(owner, isHost, members, hasMama ? mama : (ushort?)null, key);
        }
        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException($"Section {SectionId} has {stream.Length - stream.Position} trailing bytes.");
        }
        return new FamilyWorldRecord(families);
    }

    private static int ReadCount(BinaryReader reader, int max, string what)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > max)
        {
            throw new InvalidDataException($"Section {SectionId}: {what} count {count} is outside 0..{max}.");
        }
        return count;
    }
}
