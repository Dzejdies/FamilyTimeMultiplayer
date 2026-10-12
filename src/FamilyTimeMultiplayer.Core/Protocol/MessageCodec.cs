using System;
using System.IO;
using System.Text;

namespace FamilyTimeMultiplayer.Core.Protocol;

public static class ProtocolConstants
{
    public const int Version = 22;
    public static bool IsCompatibleVersion(int remoteVersion) => remoteVersion == Version;
    public const long MaxWorldBytes = 1024L * 1024 * 1024;
    public static void RequireWorldLength(long length)
    {
        if (length <= 0 || length > MaxWorldBytes) throw new InvalidDataException($"World size {length} is outside 1..{MaxWorldBytes} bytes.");
    }
    public const int WorldChunkBytes = 512 * 1024;
    public const int MaxFrameBytes = 8 * 1024 * 1024;
    public const int MaxAgents = ushort.MaxValue;
    public const int MaxPlayers = 64;
    public const int MaxRigids = 1 << 16;
    public const int DefaultPort = 27777;
}

public static class MessageCodec
{
    public static byte[] Encode(INetMessage message)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)message.Type);
            message.Write(writer);
        }
        if (stream.Length > ProtocolConstants.MaxFrameBytes)
        {
            throw new InvalidOperationException(
                $"{message.Type} encodes to {stream.Length} bytes, above the {ProtocolConstants.MaxFrameBytes} byte frame limit.");
        }
        return stream.ToArray();
    }

    public static INetMessage Decode(byte[] payload)
    {
        if (payload.Length == 0)
        {
            throw new InvalidDataException("Empty frame has no message type.");
        }
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        var type = (MessageType)reader.ReadByte();
        INetMessage message = type switch
        {
            MessageType.Addon => AddonMessage.Read(reader),
            MessageType.GenericEntityState => GenericEntityStateMessage.Read(reader),
            MessageType.GenericActionRequest => GenericActionRequestMessage.Read(reader),
            MessageType.GenericActionResult => GenericActionResultMessage.Read(reader),
            MessageType.SignedClient => SignedClientMessage.Read(reader),
            MessageType.IdentityChallenge => IdentityChallengeMessage.Read(reader),
            MessageType.IdentityProof => IdentityProofMessage.Read(reader),
            MessageType.FamilyApprovalPending => FamilyApprovalPendingMessage.Read(reader),
            MessageType.Hello => HelloMessage.Read(reader),
            MessageType.Welcome => WelcomeMessage.Read(reader),
            MessageType.Reject => RejectMessage.Read(reader),
            MessageType.WorldChunk => WorldChunkMessage.Read(reader),
            MessageType.WorldComplete => WorldCompleteMessage.Read(reader),
            MessageType.ClientReady => ClientReadyMessage.Read(reader),
            MessageType.ClientPose => ClientPoseMessage.Read(reader),
            MessageType.AgentSnapshot => AgentSnapshotMessage.Read(reader),
            MessageType.AgentSpawn => AgentSpawnMessage.Read(reader),
            MessageType.AgentDespawn => AgentDespawnMessage.Read(reader),
            MessageType.DayState => DayStateMessage.Read(reader),
            MessageType.PlayerRoster => PlayerRosterMessage.Read(reader),
            MessageType.RigidSnapshot => RigidSnapshotMessage.Read(reader),
            MessageType.RigidSpawn => RigidSpawnMessage.Read(reader),
            MessageType.RigidDespawn => RigidDespawnMessage.Read(reader),
            MessageType.PropChange => PropChangeMessage.Read(reader),
            MessageType.CommandIntent => CommandIntentMessage.Read(reader),
            MessageType.CommandFeedback => CommandFeedbackMessage.Read(reader),
            MessageType.RigidIndexMap => RigidIndexMapMessage.Read(reader),
            MessageType.FamilyStatus => FamilyStatusMessage.Read(reader),
            MessageType.BedRequest => BedRequestMessage.Read(reader),
            MessageType.BedStatus => BedStatusMessage.Read(reader),
            MessageType.NightOutcome => NightOutcomeMessage.Read(reader),
            MessageType.MamaRoster => MamaRosterMessage.Read(reader),
            MessageType.Hold => HoldMessage.Read(reader),
            MessageType.HoldResult => HoldResultMessage.Read(reader),
            MessageType.MamaToggle => MamaToggleMessage.Read(reader),
            MessageType.MamaState => MamaStateMessage.Read(reader),
            MessageType.WaterView => WaterViewMessage.Read(reader),
            MessageType.WaterTiles => WaterTilesMessage.Read(reader),
            MessageType.ToolEdits => ToolEditsMessage.Read(reader),
            MessageType.WorldEdit => WorldEditMessage.Read(reader),
            MessageType.AgentInfo => AgentInfoMessage.Read(reader),
            MessageType.AnimationEvents => AnimationEventsMessage.Read(reader),
            MessageType.CommandOutlines => CommandOutlinesMessage.Read(reader),
            MessageType.CommandMarkers => CommandMarkersMessage.Read(reader),
            MessageType.CommandAudio => CommandAudioMessage.Read(reader),
            _ => throw new InvalidDataException($"Unknown message type {(byte)type}."),
        };
        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException($"{type} frame has {stream.Length - stream.Position} trailing bytes.");
        }
        return message;
    }
}

public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(byte[] data) => Append(0u, data, 0, data.Length);

    public static uint Append(uint crc, byte[] data, int offset, int count)
    {
        uint value = ~crc;
        for (int i = offset; i < offset + count; i++)
        {
            value = Table[(value ^ data[i]) & 0xFF] ^ (value >> 8);
        }
        return ~value;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint entry = i;
            for (int bit = 0; bit < 8; bit++)
            {
                entry = (entry & 1) != 0 ? 0xEDB88320u ^ (entry >> 1) : entry >> 1;
            }
            table[i] = entry;
        }
        return table;
    }
}
