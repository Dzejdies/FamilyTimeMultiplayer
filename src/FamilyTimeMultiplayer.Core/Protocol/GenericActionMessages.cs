using System;
using System.IO;
using FamilyTimeMultiplayer.Core.Replication;

namespace FamilyTimeMultiplayer.Core.Protocol;

public enum GenericActionOutcome : byte { Accepted, Executed, Rejected, Failed, Unknown }

public sealed class GenericActionRequestMessage : INetMessage
{
    public const byte CurrentEnvelopeVersion = 1;
    public const int MaxActionIdCharacters = 128;
    public const int MaxPayloadBytes = 64 * 1024;

    public GenericActionRequestMessage(string actionId, int schemaVersion, Guid requestId, long worldGeneration,
        NetworkEntityId? target, ulong lifecycleRevision, byte[] payload)
    {
        if (string.IsNullOrWhiteSpace(actionId) || actionId.Length > MaxActionIdCharacters) throw new ArgumentException("Action id is invalid.", nameof(actionId));
        if (schemaVersion <= 0 || requestId == Guid.Empty || worldGeneration <= 0) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (target.HasValue != (lifecycleRevision > 0)) throw new ArgumentException("Target and lifecycle revision must be specified together.");
        if (target.HasValue && target.Value.WorldGeneration != worldGeneration) throw new ArgumentException("Target belongs to another generation.", nameof(target));
        if (payload == null || payload.Length > MaxPayloadBytes) throw new ArgumentException("Action payload is invalid or too large.", nameof(payload));
        ActionId = actionId.Trim(); SchemaVersion = schemaVersion; RequestId = requestId; WorldGeneration = worldGeneration;
        Target = target; LifecycleRevision = lifecycleRevision; Payload = (byte[])payload.Clone();
    }

    public MessageType Type => MessageType.GenericActionRequest;
    public string ActionId { get; }
    public int SchemaVersion { get; }
    public Guid RequestId { get; }
    public long WorldGeneration { get; }
    public NetworkEntityId? Target { get; }
    public ulong LifecycleRevision { get; }
    public ReadOnlyMemory<byte> Payload { get; }
    public void Write(BinaryWriter writer)
    {
        writer.Write(CurrentEnvelopeVersion); writer.Write(ActionId); writer.Write(SchemaVersion); writer.Write(RequestId.ToByteArray());
        writer.Write(WorldGeneration); writer.Write(Target.HasValue);
        if (Target is NetworkEntityId target) { writer.Write(target.Kind.Name); writer.Write(target.NetworkId); writer.Write(LifecycleRevision); }
        writer.Write(Payload.Length); writer.Write(Payload.ToArray());
    }
    public static GenericActionRequestMessage Read(BinaryReader reader)
    {
        if (reader.ReadByte() != CurrentEnvelopeVersion) throw new InvalidDataException("Unsupported generic action envelope version.");
        string action = ProtocolGuards.ReadText(reader, MaxActionIdCharacters, "action id"); int schema = reader.ReadInt32();
        byte[] guid = reader.ReadBytes(16); if (guid.Length != 16) throw new EndOfStreamException();
        long generation = reader.ReadInt64(); NetworkEntityId? target = null; ulong revision = 0;
        if (reader.ReadBoolean()) { string kind = ProtocolGuards.ReadText(reader, GenericEntityStateMessage.MaxEntityKindCharacters, "entity kind"); ulong id = reader.ReadUInt64(); revision = reader.ReadUInt64(); target = new NetworkEntityId(generation, new EntityKind(kind), id); }
        int length = reader.ReadInt32(); if (length < 0 || length > MaxPayloadBytes || length > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("Action payload exceeds its limit.");
        byte[] payload = reader.ReadBytes(length);
        try { return new GenericActionRequestMessage(action, schema, new Guid(guid), generation, target, revision, payload); }
        catch (ArgumentException exception) { throw new InvalidDataException("Invalid action request.", exception); }
    }
}

public sealed class GenericActionResultMessage : INetMessage
{
    public GenericActionResultMessage(Guid requestId, GenericActionOutcome outcome, string detail = "")
    {
        if (requestId == Guid.Empty) throw new ArgumentException("Request id is required.", nameof(requestId));
        if (detail.Length > 512) throw new ArgumentException("Action result detail is too long.", nameof(detail));
        RequestId = requestId; Outcome = outcome; Detail = detail;
    }
    public MessageType Type => MessageType.GenericActionResult;
    public Guid RequestId { get; }
    public GenericActionOutcome Outcome { get; }
    public string Detail { get; }
    public void Write(BinaryWriter writer) { byte[] detail = System.Text.Encoding.UTF8.GetBytes(Detail); writer.Write(RequestId.ToByteArray()); writer.Write((byte)Outcome); writer.Write(detail.Length); writer.Write(detail); }
    public static GenericActionResultMessage Read(BinaryReader reader)
    {
        byte[] id = reader.ReadBytes(16); if (id.Length != 16) throw new EndOfStreamException();
        GenericActionOutcome outcome = (GenericActionOutcome)reader.ReadByte(); if (!Enum.IsDefined(typeof(GenericActionOutcome), outcome)) throw new InvalidDataException("Invalid action outcome.");
        int length = reader.ReadInt32(); if (length < 0 || length > 2048 || length > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("Invalid action result detail length.");
        string detail; try { detail = new System.Text.UTF8Encoding(false, true).GetString(reader.ReadBytes(length)); } catch (System.Text.DecoderFallbackException exception) { throw new InvalidDataException("Invalid action result detail.", exception); }
        return new GenericActionResultMessage(new Guid(id), outcome, detail);
    }
}
