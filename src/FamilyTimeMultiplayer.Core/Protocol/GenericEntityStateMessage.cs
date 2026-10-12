using System;
using System.IO;
using FamilyTimeMultiplayer.Core.Replication;

namespace FamilyTimeMultiplayer.Core.Protocol;

public sealed class GenericEntityStateMessage : INetMessage
{
    public const byte CurrentEnvelopeVersion = 1;
    public const int MaxPayloadBytes = 256 * 1024;
    public const int MaxEntityKindCharacters = 64;
    public const int MaxDomainCharacters = 128;

    public GenericEntityStateMessage(
        NetworkEntityId identity,
        ulong entityLifecycleRevision,
        EntityStateDomain stateDomain,
        int schemaVersion,
        ulong stateRevision,
        byte[] payload)
    {
        if (identity.WorldGeneration <= 0) throw new ArgumentOutOfRangeException(nameof(identity));
        if (entityLifecycleRevision == 0) throw new ArgumentOutOfRangeException(nameof(entityLifecycleRevision));
        if (string.IsNullOrWhiteSpace(identity.Kind.Name) || identity.Kind.Name.Length > MaxEntityKindCharacters)
            throw new ArgumentException("Entity kind is invalid or too long.", nameof(identity));
        if (string.IsNullOrWhiteSpace(stateDomain.Name) || stateDomain.Name.Length > MaxDomainCharacters)
            throw new ArgumentException("State domain is invalid or too long.", nameof(stateDomain));
        if (schemaVersion <= 0) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (stateRevision == 0) throw new ArgumentOutOfRangeException(nameof(stateRevision));
        if (payload == null) throw new ArgumentNullException(nameof(payload));
        if (payload.Length > MaxPayloadBytes) throw new ArgumentException("State payload exceeds its size limit.", nameof(payload));

        Identity = identity;
        EntityLifecycleRevision = entityLifecycleRevision;
        StateDomain = stateDomain;
        SchemaVersion = schemaVersion;
        StateRevision = stateRevision;
        Payload = (byte[])payload.Clone();
    }

    public MessageType Type => MessageType.GenericEntityState;
    public NetworkEntityId Identity { get; }
    public ulong EntityLifecycleRevision { get; }
    public EntityStateDomain StateDomain { get; }
    public int SchemaVersion { get; }
    public ulong StateRevision { get; }
    public ReadOnlyMemory<byte> Payload { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(CurrentEnvelopeVersion);
        writer.Write(Identity.WorldGeneration);
        writer.Write(Identity.Kind.Name);
        writer.Write(Identity.NetworkId);
        writer.Write(EntityLifecycleRevision);
        writer.Write(StateDomain.Name);
        writer.Write(SchemaVersion);
        writer.Write(StateRevision);
        writer.Write(Payload.Length);
        writer.Write(Payload.ToArray());
    }

    public static GenericEntityStateMessage Read(BinaryReader reader)
    {
        byte version = reader.ReadByte();
        if (version != CurrentEnvelopeVersion) throw new InvalidDataException($"Unsupported generic state envelope version {version}.");
        long generation = reader.ReadInt64();
        string kind = ProtocolGuards.ReadText(reader, MaxEntityKindCharacters, "entity kind");
        ulong networkId = reader.ReadUInt64();
        ulong lifecycleRevision = reader.ReadUInt64();
        string domain = ProtocolGuards.ReadText(reader, MaxDomainCharacters, "state domain");
        int schemaVersion = reader.ReadInt32();
        ulong revision = reader.ReadUInt64();
        int payloadLength = reader.ReadInt32();
        if (payloadLength < 0 || payloadLength > MaxPayloadBytes || payloadLength > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Generic state payload exceeds its size limit or frame boundary.");
        byte[] payload = reader.ReadBytes(payloadLength);
        if (payload.Length != payloadLength) throw new EndOfStreamException("Generic state payload is truncated.");
        try
        {
            return new GenericEntityStateMessage(
                new NetworkEntityId(generation, new EntityKind(kind), networkId),
                lifecycleRevision, new EntityStateDomain(domain), schemaVersion, revision, payload);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Generic state envelope contains invalid fields.", exception);
        }
    }
}
