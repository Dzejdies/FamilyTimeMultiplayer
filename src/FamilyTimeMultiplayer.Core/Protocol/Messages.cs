using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FamilyTimeMultiplayer.Core.Protocol;

public enum MessageType : byte
{
    Hello = 1,
    Welcome = 2,
    Reject = 3,
    WorldChunk = 4,
    WorldComplete = 5,
    ClientReady = 6,
    ClientPose = 7,
    AgentSnapshot = 8,
    AgentSpawn = 9,
    AgentDespawn = 10,
    DayState = 11,
    PlayerRoster = 12,
    RigidSnapshot = 13,
    RigidSpawn = 14,
    RigidDespawn = 15,
    PropChange = 16,
    CommandIntent = 17,
    CommandFeedback = 18,
    RigidIndexMap = 19,
    FamilyStatus = 20,
    BedRequest = 21,
    BedStatus = 22,
    NightOutcome = 23,
    MamaRoster = 24,
    Hold = 25,
    HoldResult = 26,
    MamaToggle = 27,
    MamaState = 28,
    WorldEdit = 29,
    ToolEdits = 35,
    WaterView = 36,
    WaterTiles = 37,
    AgentInfo = 30,
    AnimationEvents = 31,
    CommandAudio = 32,
    CommandMarkers = 33,
    CommandOutlines = 34,
    IdentityChallenge = 38,
    IdentityProof = 39,
    FamilyApprovalPending = 40,
    SignedClient = 41,
    Addon = 42,
}

public interface INetMessage
{
    MessageType Type { get; }

    void Write(BinaryWriter writer);
}

public sealed class HelloMessage : INetMessage
{
    public HelloMessage(int protocolVersion, string gameBuildId, string playerName, string publicKey = "", IEnumerable<AddonDescriptor>? addons = null)
    {
        ProtocolVersion = protocolVersion;
        GameBuildId = gameBuildId;
        PlayerName = playerName;
        PublicKey = publicKey;
        Addons = AddonCapabilities.Normalize(addons ?? Array.Empty<AddonDescriptor>());
    }

    public MessageType Type => MessageType.Hello;
    public int ProtocolVersion { get; }
    public string GameBuildId { get; }
    public string PlayerName { get; }
    public string PublicKey { get; }
    public IReadOnlyList<AddonDescriptor> Addons { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(ProtocolVersion);
        writer.Write(GameBuildId);
        writer.Write(PlayerName);
        writer.Write(PublicKey);
        writer.Write((byte)Addons.Count);
        foreach (AddonDescriptor addon in Addons) addon.Write(writer);
    }

    public static HelloMessage Read(BinaryReader reader) =>
        new HelloMessage(reader.ReadInt32(), ProtocolGuards.ReadText(reader, 128, "game build"), ProtocolGuards.ReadText(reader, 64, "player name"), ProtocolGuards.ReadText(reader, 512, "player identity"), AddonCapabilities.Read(reader));
}

public enum FamilyMode : byte
{
    Shared = 1,
    Separate = 2,
}

public sealed class WelcomeMessage : INetMessage
{
    public WelcomeMessage(int playerId, ushort avatarStableIndex, long worldByteLength, FamilyMode familyMode, long transferId = 1, long worldGeneration = 1)
    {
        PlayerId = playerId;
        AvatarStableIndex = avatarStableIndex;
        WorldByteLength = worldByteLength;
        FamilyMode = familyMode;
        if (transferId <= 0) throw new InvalidDataException("Invalid world transfer identity.");
        TransferId = transferId;
        if (worldGeneration < 1) throw new InvalidDataException("Invalid addon world generation.");
        WorldGeneration = worldGeneration;
    }

    public MessageType Type => MessageType.Welcome;
    public int PlayerId { get; }
    public ushort AvatarStableIndex { get; }
    public long WorldByteLength { get; }
    public FamilyMode FamilyMode { get; }
    public long TransferId { get; }
    public long WorldGeneration { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(PlayerId);
        writer.Write(AvatarStableIndex);
        writer.Write(WorldByteLength);
        writer.Write((byte)FamilyMode);
        writer.Write(TransferId);
        writer.Write(WorldGeneration);
    }

    public static WelcomeMessage Read(BinaryReader reader)
    {
        int playerId = reader.ReadInt32();
        ushort avatar = reader.ReadUInt16();
        long length = reader.ReadInt64();
        var mode = (FamilyMode)reader.ReadByte();
        ProtocolConstants.RequireWorldLength(length);
        if (mode != FamilyMode.Shared && mode != FamilyMode.Separate)
        {
            throw new InvalidDataException($"Welcome announces unknown family mode {(byte)mode}.");
        }
        return new WelcomeMessage(playerId, avatar, length, mode, reader.ReadInt64(), reader.ReadInt64());
    }
}

public readonly struct RosterEntry
{
    public readonly int PlayerId;
    public readonly string Name;
    public readonly ushort AvatarStableIndex;

    public RosterEntry(int playerId, string name, ushort avatarStableIndex)
    {
        PlayerId = playerId;
        Name = name;
        AvatarStableIndex = avatarStableIndex;
    }
}

public sealed class PlayerRosterMessage : INetMessage
{
    public const int HostPlayerId = 0;

    public PlayerRosterMessage(IReadOnlyList<RosterEntry> players)
    {
        Players = players;
    }

    public MessageType Type => MessageType.PlayerRoster;
    public IReadOnlyList<RosterEntry> Players { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Players.Count);
        for (int i = 0; i < Players.Count; i++)
        {
            writer.Write(Players[i].PlayerId);
            writer.Write(Players[i].Name);
            writer.Write(Players[i].AvatarStableIndex);
        }
    }

    public static PlayerRosterMessage Read(BinaryReader reader)
    {
        int count = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxPlayers, "player roster");
        var players = new RosterEntry[count];
        for (int i = 0; i < count; i++)
        {
            players[i] = new RosterEntry(reader.ReadInt32(), reader.ReadString(), reader.ReadUInt16());
        }
        return new PlayerRosterMessage(players);
    }
}

public sealed class RejectMessage : INetMessage
{
    public RejectMessage(string reason)
    {
        Reason = reason;
    }

    public MessageType Type => MessageType.Reject;
    public string Reason { get; }

    public void Write(BinaryWriter writer) => writer.Write(Reason);

    public static RejectMessage Read(BinaryReader reader) => new RejectMessage(reader.ReadString());
}

public sealed class WorldChunkMessage : INetMessage
{
    public WorldChunkMessage(long offset, byte[] data)
    {
        Offset = offset;
        Data = data;
    }

    public MessageType Type => MessageType.WorldChunk;
    public long Offset { get; }
    public byte[] Data { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Offset);
        writer.Write(Data.Length);
        writer.Write(Data);
    }

    public static WorldChunkMessage Read(BinaryReader reader)
    {
        long offset = reader.ReadInt64();
        int length = reader.ReadInt32();
        if (length < 0 || length > ProtocolConstants.WorldChunkBytes)
        {
            throw new InvalidDataException($"World chunk length {length} is outside 0..{ProtocolConstants.WorldChunkBytes}.");
        }
        return new WorldChunkMessage(offset, reader.ReadBytes(length));
    }
}

public sealed class WorldCompleteMessage : INetMessage
{
    public WorldCompleteMessage(uint crc32)
    {
        Crc32 = crc32;
    }

    public MessageType Type => MessageType.WorldComplete;
    public uint Crc32 { get; }

    public void Write(BinaryWriter writer) => writer.Write(Crc32);

    public static WorldCompleteMessage Read(BinaryReader reader) => new WorldCompleteMessage(reader.ReadUInt32());
}

public sealed class ClientReadyMessage : INetMessage
{
    public ClientReadyMessage(long transferId = 1, long worldGeneration = 1)
    {
        if (transferId <= 0) throw new InvalidDataException("Invalid ready transfer identity.");
        TransferId = transferId;
    }
    public long TransferId { get; }
    public MessageType Type => MessageType.ClientReady;
    public void Write(BinaryWriter writer) => writer.Write(TransferId);
    public static ClientReadyMessage Read(BinaryReader reader) => new ClientReadyMessage(reader.ReadInt64());
}

public sealed class ClientPoseMessage : INetMessage
{
    public ClientPoseMessage(NetVector3 position, float yawDegrees, float pitchDegrees, NetVector3 velocity)
    {
        Position = position;
        YawDegrees = yawDegrees;
        PitchDegrees = pitchDegrees;
        Velocity = velocity;
    }

    public MessageType Type => MessageType.ClientPose;
    public NetVector3 Position { get; }
    public float YawDegrees { get; }
    public float PitchDegrees { get; }
    public NetVector3 Velocity { get; }

    public void Write(BinaryWriter writer)
    {
        Position.Write(writer);
        writer.Write(YawDegrees);
        writer.Write(PitchDegrees);
        Velocity.Write(writer);
    }

    public static ClientPoseMessage Read(BinaryReader reader)
    {
        var pose = new ClientPoseMessage(NetVector3.Read(reader), reader.ReadSingle(), reader.ReadSingle(), NetVector3.Read(reader));
        if (!Safety.MotionBudget.ValidPose(pose)) throw new InvalidDataException($"Invalid client pose: position={pose.Position}, velocity={pose.Velocity}, yaw={pose.YawDegrees}, pitch={pose.PitchDegrees}.");
        return pose;
    }
}

public readonly struct AgentState
{
    public readonly ushort StableIndex;
    public readonly NetPose Pose;

    public AgentState(ushort stableIndex, NetPose pose)
    {
        StableIndex = stableIndex;
        Pose = pose;
    }
}

public sealed class AgentSnapshotMessage : INetMessage
{
    public AgentSnapshotMessage(double hostTimeSeconds, IReadOnlyList<AgentState> agents)
    {
        HostTimeSeconds = hostTimeSeconds;
        Agents = agents;
    }

    public MessageType Type => MessageType.AgentSnapshot;
    public double HostTimeSeconds { get; }
    public IReadOnlyList<AgentState> Agents { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(HostTimeSeconds);
        writer.Write(Agents.Count);
        for (int i = 0; i < Agents.Count; i++)
        {
            writer.Write(Agents[i].StableIndex);
            Agents[i].Pose.Write(writer);
        }
    }

    public static AgentSnapshotMessage Read(BinaryReader reader)
    {
        double hostTime = reader.ReadDouble();
        int count = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxAgents, "agent snapshot");
        var agents = new AgentState[count];
        for (int i = 0; i < count; i++)
        {
            agents[i] = new AgentState(reader.ReadUInt16(), NetPose.Read(reader));
        }
        return new AgentSnapshotMessage(hostTime, agents);
    }
}

public readonly struct AgentSpawn
{
    public readonly ushort StableIndex;
    public readonly int TemplateIndex;
    public readonly NetPose Pose;

    public AgentSpawn(ushort stableIndex, int templateIndex, NetPose pose)
    {
        StableIndex = stableIndex;
        TemplateIndex = templateIndex;
        Pose = pose;
    }
}

public sealed class AgentSpawnMessage : INetMessage
{
    public AgentSpawnMessage(IReadOnlyList<AgentSpawn> spawns)
    {
        Spawns = spawns;
    }

    public MessageType Type => MessageType.AgentSpawn;
    public IReadOnlyList<AgentSpawn> Spawns { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Spawns.Count);
        for (int i = 0; i < Spawns.Count; i++)
        {
            writer.Write(Spawns[i].StableIndex);
            writer.Write(Spawns[i].TemplateIndex);
            Spawns[i].Pose.Write(writer);
        }
    }

    public static AgentSpawnMessage Read(BinaryReader reader)
    {
        int count = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxAgents, "agent spawn");
        var spawns = new AgentSpawn[count];
        for (int i = 0; i < count; i++)
        {
            spawns[i] = new AgentSpawn(reader.ReadUInt16(), reader.ReadInt32(), NetPose.Read(reader));
        }
        return new AgentSpawnMessage(spawns);
    }
}

public sealed class AgentDespawnMessage : INetMessage
{
    public AgentDespawnMessage(IReadOnlyList<ushort> stableIndices)
    {
        StableIndices = stableIndices;
    }

    public MessageType Type => MessageType.AgentDespawn;
    public IReadOnlyList<ushort> StableIndices { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(StableIndices.Count);
        for (int i = 0; i < StableIndices.Count; i++)
        {
            writer.Write(StableIndices[i]);
        }
    }

    public static AgentDespawnMessage Read(BinaryReader reader)
    {
        int count = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxAgents, "agent despawn");
        var indices = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            indices[i] = reader.ReadUInt16();
        }
        return new AgentDespawnMessage(indices);
    }
}

public enum DailyNeed : byte
{
    Food = 0,
    Water = 1,
}

public sealed class DayStateMessage : INetMessage
{
    public DayStateMessage(int day, float timer, float startTimeOfDay, float endTimeOfDay, bool isMoving, DailyNeed need = DailyNeed.Food)
    {
        if (need != DailyNeed.Food && need != DailyNeed.Water) throw new ArgumentOutOfRangeException(nameof(need));
        Need = need;
        Day = day;
        Timer = timer;
        StartTimeOfDay = startTimeOfDay;
        EndTimeOfDay = endTimeOfDay;
        IsMoving = isMoving;
    }

    public DailyNeed Need { get; }
    public MessageType Type => MessageType.DayState;
    public int Day { get; }
    public float Timer { get; }
    public float StartTimeOfDay { get; }
    public float EndTimeOfDay { get; }
    public bool IsMoving { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Day);
        writer.Write(Timer);
        writer.Write(StartTimeOfDay);
        writer.Write(EndTimeOfDay);
        writer.Write(IsMoving);
        writer.Write((byte)Need);
    }

    public static DayStateMessage Read(BinaryReader reader)
    {
        int day = reader.ReadInt32();
        float timer = reader.ReadSingle();
        float start = reader.ReadSingle();
        float end = reader.ReadSingle();
        bool moving = reader.ReadBoolean();
        var need = (DailyNeed)reader.ReadByte();
        if (need != DailyNeed.Food && need != DailyNeed.Water) throw new InvalidDataException("Unknown daily need.");
        return new DayStateMessage(day, timer, start, end, moving, need);
    }
}

internal static class ProtocolGuards
{
    public static string ReadText(BinaryReader reader, int maximum, string field)
    {
        int count = ReadTextByteCount(reader);
        if (count > checked(maximum * 4) || count > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException($"{field} exceeds its bounded frame or text limit.");
        byte[] bytes = reader.ReadBytes(count);
        string value;
        try { value = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException($"{field} is not valid UTF8.", exception); }
        if (value.Length > maximum) throw new InvalidDataException($"{field} exceeds its {maximum} character limit.");
        return value;
    }

    private static int ReadTextByteCount(BinaryReader reader)
    {
        uint count = 0;
        for (int part = 0; part < 5; part++)
        {
            byte value = reader.ReadByte();
            if (part == 4 && value > 7) throw new InvalidDataException("Invalid text byte count.");
            count |= (uint)(value & 127) << (7 * part);
            if ((value & 128) == 0) return (int)count;
        }
        throw new InvalidDataException("Invalid text byte count.");
    }

    public static int ReadCount(BinaryReader reader, int max, string context)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > max)
        {
            throw new InvalidDataException($"{context} count {count} is outside 0..{max}.");
        }
        return count;
    }
}

public readonly struct RigidState
{
    public readonly int NetId;
    public readonly NetVector3 Position;
    public readonly NetQuaternion Rotation;
    public readonly bool Carried;

    public RigidState(int netId, NetVector3 position, NetQuaternion rotation, bool carried)
    {
        NetId = netId;
        Position = position;
        Rotation = rotation;
        Carried = carried;
    }
}

public sealed class RigidSnapshotMessage : INetMessage
{
    public RigidSnapshotMessage(double hostTimeSeconds, IReadOnlyList<RigidState> rigids)
    {
        HostTimeSeconds = hostTimeSeconds;
        Rigids = rigids;
    }

    public MessageType Type => MessageType.RigidSnapshot;
    public double HostTimeSeconds { get; }
    public IReadOnlyList<RigidState> Rigids { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(HostTimeSeconds);
        writer.Write(Rigids.Count);
        for (int i = 0; i < Rigids.Count; i++)
        {
            writer.Write(Rigids[i].NetId);
            Rigids[i].Position.Write(writer);
            Rigids[i].Rotation.Write(writer);
            writer.Write(Rigids[i].Carried);
        }
    }

    public static RigidSnapshotMessage Read(BinaryReader reader)
    {
        double hostTime = reader.ReadDouble();
        int count = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxRigids, "rigid snapshot");
        var rigids = new RigidState[count];
        for (int i = 0; i < count; i++)
        {
            rigids[i] = new RigidState(reader.ReadInt32(), NetVector3.Read(reader), NetQuaternion.Read(reader), reader.ReadBoolean());
        }
        return new RigidSnapshotMessage(hostTime, rigids);
    }
}

public readonly struct RigidSpawn
{
    public readonly int NetId;
    public readonly int PrefabIndex;
    public readonly NetVector3 Position;
    public readonly NetQuaternion Rotation;

    public RigidSpawn(int netId, int prefabIndex, NetVector3 position, NetQuaternion rotation)
    {
        NetId = netId;
        PrefabIndex = prefabIndex;
        Position = position;
        Rotation = rotation;
    }
}

public sealed class RigidSpawnMessage : INetMessage
{
    public RigidSpawnMessage(IReadOnlyList<RigidSpawn> spawns)
    {
        Spawns = spawns;
    }

    public MessageType Type => MessageType.RigidSpawn;
    public IReadOnlyList<RigidSpawn> Spawns { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Spawns.Count);
        for (int i = 0; i < Spawns.Count; i++)
        {
            writer.Write(Spawns[i].NetId);
            writer.Write(Spawns[i].PrefabIndex);
            Spawns[i].Position.Write(writer);
            Spawns[i].Rotation.Write(writer);
        }
    }

    public static RigidSpawnMessage Read(BinaryReader reader)
    {
        int count = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxRigids, "rigid spawn");
        var spawns = new RigidSpawn[count];
        for (int i = 0; i < count; i++)
        {
            spawns[i] = new RigidSpawn(reader.ReadInt32(), reader.ReadInt32(), NetVector3.Read(reader), NetQuaternion.Read(reader));
        }
        return new RigidSpawnMessage(spawns);
    }
}

public sealed class RigidDespawnMessage : INetMessage
{
    public RigidDespawnMessage(IReadOnlyList<int> netIds)
    {
        NetIds = netIds;
    }

    public MessageType Type => MessageType.RigidDespawn;
    public IReadOnlyList<int> NetIds { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(NetIds.Count);
        for (int i = 0; i < NetIds.Count; i++)
        {
            writer.Write(NetIds[i]);
        }
    }

    public static RigidDespawnMessage Read(BinaryReader reader)
    {
        int count = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxRigids, "rigid despawn");
        var ids = new int[count];
        for (int i = 0; i < count; i++)
        {
            ids[i] = reader.ReadInt32();
        }
        return new RigidDespawnMessage(ids);
    }
}

public readonly struct PropChange
{
    public readonly uint CellX;
    public readonly uint CellZ;
    public readonly bool Added;
    public readonly string PropName;

    public PropChange(uint cellX, uint cellZ, bool added, string propName)
    {
        CellX = cellX;
        CellZ = cellZ;
        Added = added;
        PropName = propName;
    }
}

public sealed class PropChangeMessage : INetMessage
{
    public PropChangeMessage(IReadOnlyList<PropChange> changes)
    {
        Changes = changes;
    }

    public MessageType Type => MessageType.PropChange;
    public IReadOnlyList<PropChange> Changes { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Changes.Count);
        for (int i = 0; i < Changes.Count; i++)
        {
            writer.Write(Changes[i].CellX);
            writer.Write(Changes[i].CellZ);
            writer.Write(Changes[i].Added);
            writer.Write(Changes[i].PropName);
        }
    }

    public static PropChangeMessage Read(BinaryReader reader)
    {
        int count = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxRigids, "prop change");
        var changes = new PropChange[count];
        for (int i = 0; i < count; i++)
        {
            changes[i] = new PropChange(reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadBoolean(), reader.ReadString());
        }
        return new PropChangeMessage(changes);
    }
}

public enum CommandIntentKind : byte
{
    FollowPressed = 1,
    FollowReleased = 2,
    Interact = 3,
    CommandLock = 4,
    PickupSweepStarted = 5,
    PickupSweepStopped = 6,
}

public enum CommandTargetKind : byte
{
    None = 0,
    Rigid = 1,
    Prop = 2,
    Ground = 3,
    Agent = 4,
    BuildPlan = 5,
    Station = 6,
}

public sealed class CommandIntentMessage : INetMessage
{
    public CommandIntentMessage(CommandIntentKind kind, CommandTargetKind targetKind, int rigidNetId, int propCellX, int propCellZ, string propName, NetVector3 point)
    {
        Kind = kind;
        TargetKind = targetKind;
        RigidNetId = rigidNetId;
        PropCellX = propCellX;
        PropCellZ = propCellZ;
        PropName = propName;
        Point = point;
    }

    public static CommandIntentMessage Follow(bool pressed) =>
        new CommandIntentMessage(pressed ? CommandIntentKind.FollowPressed : CommandIntentKind.FollowReleased, CommandTargetKind.None, -1, 0, 0, string.Empty, NetVector3.Zero);

    public static CommandIntentMessage InteractRigid(int netId) =>
        new CommandIntentMessage(CommandIntentKind.Interact, CommandTargetKind.Rigid, netId, 0, 0, string.Empty, NetVector3.Zero);

    public static CommandIntentMessage InteractProp(int cellX, int cellZ, string propName) =>
        new CommandIntentMessage(CommandIntentKind.Interact, CommandTargetKind.Prop, -1, cellX, cellZ, propName, NetVector3.Zero);

    public static CommandIntentMessage InteractGround(NetVector3 point) =>
        new CommandIntentMessage(CommandIntentKind.Interact, CommandTargetKind.Ground, -1, 0, 0, string.Empty, point);

    public static CommandIntentMessage InteractAgent(ushort hostStableIndex, NetVector3 lastKnownPosition) =>
        new CommandIntentMessage(CommandIntentKind.Interact, CommandTargetKind.Agent, hostStableIndex, 0, 0, string.Empty, lastKnownPosition);

    public static CommandIntentMessage InteractBuildPlan(NetVector3 planCenter) =>
        new CommandIntentMessage(CommandIntentKind.Interact, CommandTargetKind.BuildPlan, -1, 0, 0, string.Empty, planCenter);

    public static CommandIntentMessage InteractStation(NetVector3 stationPosition) =>
        new CommandIntentMessage(CommandIntentKind.Interact, CommandTargetKind.Station, -1, 0, 0, string.Empty, stationPosition);

    public static CommandIntentMessage ToggleCommandLock(ushort hostStableIndex) =>
        new CommandIntentMessage(CommandIntentKind.CommandLock, CommandTargetKind.Agent, hostStableIndex, 0, 0, string.Empty, NetVector3.Zero);

    public static CommandIntentMessage PickupSweep(int rigidNetId, bool started) =>
        new CommandIntentMessage(started ? CommandIntentKind.PickupSweepStarted : CommandIntentKind.PickupSweepStopped, started ? CommandTargetKind.Rigid : CommandTargetKind.None, started ? rigidNetId : -1, 0, 0, string.Empty, NetVector3.Zero);

    public MessageType Type => MessageType.CommandIntent;
    public CommandIntentKind Kind { get; }
    public CommandTargetKind TargetKind { get; }
    public int RigidNetId { get; }
    public int PropCellX { get; }
    public int PropCellZ { get; }
    public string PropName { get; }
    public NetVector3 Point { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write((byte)Kind);
        writer.Write((byte)TargetKind);
        writer.Write(RigidNetId);
        writer.Write(PropCellX);
        writer.Write(PropCellZ);
        writer.Write(PropName);
        Point.Write(writer);
    }

    public static CommandIntentMessage Read(BinaryReader reader)
    {
        var kind = (CommandIntentKind)reader.ReadByte();
        var target = (CommandTargetKind)reader.ReadByte();
        if (kind < CommandIntentKind.FollowPressed || kind > CommandIntentKind.PickupSweepStopped)
        {
            throw new InvalidDataException($"Unknown command intent {(byte)kind}.");
        }
        if (target > CommandTargetKind.Station)
        {
            throw new InvalidDataException($"Unknown command target {(byte)target}.");
        }
        return new CommandIntentMessage(kind, target, reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), ProtocolGuards.ReadText(reader, 128, "prop name"), NetVector3.Read(reader));
    }
}

public enum CommandOutcome : byte
{
    None = 0,
    Accepted = 1,
    Refused = 2,
}

public sealed class CommandFeedbackMessage : INetMessage
{
    public CommandFeedbackMessage(int availableWorkers, int totalWorkers, CommandOutcome outcome, string detail)
    {
        AvailableWorkers = availableWorkers;
        TotalWorkers = totalWorkers;
        Outcome = outcome;
        Detail = detail;
    }

    public MessageType Type => MessageType.CommandFeedback;
    public int AvailableWorkers { get; }
    public int TotalWorkers { get; }
    public CommandOutcome Outcome { get; }
    public string Detail { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(AvailableWorkers);
        writer.Write(TotalWorkers);
        writer.Write((byte)Outcome);
        writer.Write(Detail);
    }

    public static CommandFeedbackMessage Read(BinaryReader reader)
    {
        int available = reader.ReadInt32();
        int total = reader.ReadInt32();
        var outcome = (CommandOutcome)reader.ReadByte();
        if (outcome > CommandOutcome.Refused)
        {
            throw new InvalidDataException($"Unknown command outcome {(byte)outcome}.");
        }
        return new CommandFeedbackMessage(available, total, outcome, reader.ReadString());
    }
}

public sealed class RigidIndexMapMessage : INetMessage
{
    public RigidIndexMapMessage(IReadOnlyList<int> netIdsInManagedOrder)
    {
        NetIdsInManagedOrder = netIdsInManagedOrder;
    }

    public MessageType Type => MessageType.RigidIndexMap;
    public IReadOnlyList<int> NetIdsInManagedOrder { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(NetIdsInManagedOrder.Count);
        for (int i = 0; i < NetIdsInManagedOrder.Count; i++)
        {
            writer.Write(NetIdsInManagedOrder[i]);
        }
    }

    public static RigidIndexMapMessage Read(BinaryReader reader)
    {
        int count = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxRigids, "rigid index map");
        var ids = new int[count];
        for (int i = 0; i < count; i++)
        {
            ids[i] = reader.ReadInt32();
        }
        return new RigidIndexMapMessage(ids);
    }
}

public sealed class FamilyStatusMessage : INetMessage
{
    public FamilyStatusMessage(int children, int hungry, int notSleeping, ushort[]? members = null)
    {
        if (children < 0 || hungry < 0 || notSleeping < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(children), "Family status counts cannot be negative.");
        }
        if (hungry > children || notSleeping > children || children > ProtocolConstants.MaxAgents)
        {
            throw new ArgumentOutOfRangeException(nameof(children), "Family status exceeds the family size.");
        }
        if (members != null && (members.Length != children || new HashSet<ushort>(members).Count != members.Length || Array.IndexOf(members, ushort.MaxValue) >= 0))
        {
            throw new ArgumentException("Family members must uniquely identify every child.", nameof(members));
        }
        Children = children;
        Hungry = hungry;
        NotSleeping = notSleeping;
        Members = members == null ? null : (ushort[])members.Clone();
    }

    public ushort[]? Members { get; }
    public MessageType Type => MessageType.FamilyStatus;
    public int Children { get; }
    public int Hungry { get; }
    public int NotSleeping { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Children);
        writer.Write(Hungry);
        writer.Write(NotSleeping);
        writer.Write(Members?.Length ?? -1);
        if (Members != null)
        {
            foreach (ushort member in Members) writer.Write(member);
        }
    }

    public static FamilyStatusMessage Read(BinaryReader reader)
    {
        int children = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxAgents, "family children");
        int hungry = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxAgents, "family hungry");
        int notSleeping = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxAgents, "family not sleeping");
        int count = reader.ReadInt32();
        if (count < -1 || count > ProtocolConstants.MaxAgents) throw new InvalidDataException("Invalid family member count.");
        ushort[]? members = count < 0 ? null : new ushort[count];
        if (members != null)
        {
            for (int i = 0; i < members.Length; i++) members[i] = reader.ReadUInt16();
        }
        if (hungry > children || notSleeping > children || members != null && (members.Length != children || new HashSet<ushort>(members).Count != members.Length || Array.IndexOf(members, ushort.MaxValue) >= 0))
            throw new InvalidDataException("Invalid family status membership or counts.");
        return new FamilyStatusMessage(children, hungry, notSleeping, members);
    }
}

public sealed class BedRequestMessage : INetMessage
{
    public BedRequestMessage(bool ready)
    {
        Ready = ready;
    }

    public MessageType Type => MessageType.BedRequest;
    public bool Ready { get; }

    public void Write(BinaryWriter writer) => writer.Write(Ready);

    public static BedRequestMessage Read(BinaryReader reader) => new BedRequestMessage(reader.ReadBoolean());
}

public sealed class BedStatusMessage : INetMessage
{
    public BedStatusMessage(bool youAreReady, IReadOnlyList<string> waitingFor)
    {
        YouAreReady = youAreReady;
        WaitingFor = waitingFor ?? throw new ArgumentNullException(nameof(waitingFor));
    }

    public MessageType Type => MessageType.BedStatus;
    public bool YouAreReady { get; }
    public IReadOnlyList<string> WaitingFor { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(YouAreReady);
        writer.Write(WaitingFor.Count);
        foreach (string name in WaitingFor)
        {
            writer.Write(name);
        }
    }

    public static BedStatusMessage Read(BinaryReader reader)
    {
        bool ready = reader.ReadBoolean();
        int count = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxPlayers, "bed waiting list");
        var names = new string[count];
        for (int i = 0; i < count; i++)
        {
            names[i] = reader.ReadString();
        }
        return new BedStatusMessage(ready, names);
    }
}

public sealed class NightOutcomeMessage : INetMessage
{
    public NightOutcomeMessage(bool childrenMissing, bool childrenHungry, int grownBy)
    {
        if (grownBy < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(grownBy), grownBy, "Growth cannot be negative.");
        }
        ChildrenMissing = childrenMissing;
        ChildrenHungry = childrenHungry;
        GrownBy = grownBy;
    }

    public MessageType Type => MessageType.NightOutcome;
    public bool ChildrenMissing { get; }
    public bool ChildrenHungry { get; }
    public int GrownBy { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(ChildrenMissing);
        writer.Write(ChildrenHungry);
        writer.Write(GrownBy);
    }

    public static NightOutcomeMessage Read(BinaryReader reader)
    {
        bool missing = reader.ReadBoolean();
        bool hungry = reader.ReadBoolean();
        int grown = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxAgents, "night growth");
        return new NightOutcomeMessage(missing, hungry, grown);
    }
}

public readonly struct MamaEntry
{
    public MamaEntry(string ownerName, ushort agentStableIndex)
    {
        OwnerName = ownerName ?? throw new ArgumentNullException(nameof(ownerName));
        AgentStableIndex = agentStableIndex;
    }

    public string OwnerName { get; }
    public ushort AgentStableIndex { get; }
}

public sealed class MamaRosterMessage : INetMessage
{
    public MamaRosterMessage(IReadOnlyList<MamaEntry> mamas)
    {
        Mamas = mamas ?? throw new ArgumentNullException(nameof(mamas));
    }

    public MessageType Type => MessageType.MamaRoster;
    public IReadOnlyList<MamaEntry> Mamas { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Mamas.Count);
        foreach (MamaEntry mama in Mamas)
        {
            writer.Write(mama.OwnerName);
            writer.Write(mama.AgentStableIndex);
        }
    }

    public static MamaRosterMessage Read(BinaryReader reader)
    {
        int count = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxPlayers, "mama roster");
        var mamas = new MamaEntry[count];
        for (int i = 0; i < count; i++)
        {
            mamas[i] = new MamaEntry(reader.ReadString(), reader.ReadUInt16());
        }
        return new MamaRosterMessage(mamas);
    }
}

public enum HoldTargetKind : byte
{
    Rigid = 1,
    Mama = 2,
    Agent = 3,
}

public readonly struct HoldTarget : IEquatable<HoldTarget>
{
    public HoldTarget(HoldTargetKind kind, int id)
    {
        if (kind < HoldTargetKind.Rigid || kind > HoldTargetKind.Agent)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown hold target kind.");
        }
        Kind = kind;
        Id = id;
    }

    public HoldTargetKind Kind { get; }
    public int Id { get; }

    public static HoldTarget Rigid(int netId) => new HoldTarget(HoldTargetKind.Rigid, netId);

    public static HoldTarget Mama(ushort agent) => new HoldTarget(HoldTargetKind.Mama, agent);

    public static HoldTarget Agent(ushort hostStableIndex) => new HoldTarget(HoldTargetKind.Agent, hostStableIndex);

    public bool Equals(HoldTarget other) => Kind == other.Kind && Id == other.Id;

    public override bool Equals(object? obj) => obj is HoldTarget other && Equals(other);

    public override int GetHashCode() => ((int)Kind * 397) ^ Id;

    public override string ToString() => $"{Kind}#{Id}";

    internal void Write(BinaryWriter writer)
    {
        writer.Write((byte)Kind);
        writer.Write(Id);
    }

    internal static HoldTarget Read(BinaryReader reader)
    {
        var kind = (HoldTargetKind)reader.ReadByte();
        if (kind < HoldTargetKind.Rigid || kind > HoldTargetKind.Agent)
        {
            throw new InvalidDataException($"Unknown hold target kind {(byte)kind}.");
        }
        return new HoldTarget(kind, reader.ReadInt32());
    }
}

public enum HoldPhase : byte
{
    Begin = 1,
    Update = 2,
    End = 3,
    Settle = 4,
}

public sealed class HoldMessage : INetMessage
{
    public HoldMessage(HoldTarget target, HoldPhase phase, bool carriedInHand, NetVector3 position, NetQuaternion rotation, NetVector3 velocity)
    {
        if (phase < HoldPhase.Begin || phase > HoldPhase.Settle)
        {
            throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown hold phase.");
        }
        Target = target;
        Phase = phase;
        CarriedInHand = carriedInHand;
        Position = position;
        Rotation = rotation;
        Velocity = velocity;
    }

    public MessageType Type => MessageType.Hold;
    public HoldTarget Target { get; }
    public HoldPhase Phase { get; }
    public bool CarriedInHand { get; }
    public NetVector3 Position { get; }
    public NetQuaternion Rotation { get; }
    public NetVector3 Velocity { get; }

    public void Write(BinaryWriter writer)
    {
        Target.Write(writer);
        writer.Write((byte)Phase);
        writer.Write(CarriedInHand);
        Position.Write(writer);
        Rotation.Write(writer);
        Velocity.Write(writer);
    }

    public static HoldMessage Read(BinaryReader reader)
    {
        HoldTarget target = HoldTarget.Read(reader);
        var phase = (HoldPhase)reader.ReadByte();
        if (phase < HoldPhase.Begin || phase > HoldPhase.Settle)
        {
            throw new InvalidDataException($"Unknown hold phase {(byte)phase}.");
        }
        return new HoldMessage(target, phase, reader.ReadBoolean(), NetVector3.Read(reader), NetQuaternion.Read(reader), NetVector3.Read(reader));
    }
}

public sealed class HoldResultMessage : INetMessage
{
    public HoldResultMessage(HoldTarget target, bool granted, string reason)
    {
        Target = target;
        Granted = granted;
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
    }

    public MessageType Type => MessageType.HoldResult;
    public HoldTarget Target { get; }
    public bool Granted { get; }
    public string Reason { get; }

    public void Write(BinaryWriter writer)
    {
        Target.Write(writer);
        writer.Write(Granted);
        writer.Write(Reason);
    }

    public static HoldResultMessage Read(BinaryReader reader) =>
        new HoldResultMessage(HoldTarget.Read(reader), reader.ReadBoolean(), reader.ReadString());
}

public sealed class MamaToggleMessage : INetMessage
{
    public MamaToggleMessage(ushort agent)
    {
        Agent = agent;
    }

    public MessageType Type => MessageType.MamaToggle;
    public ushort Agent { get; }

    public void Write(BinaryWriter writer) => writer.Write(Agent);

    public static MamaToggleMessage Read(BinaryReader reader) => new MamaToggleMessage(reader.ReadUInt16());
}

public readonly struct MamaStateEntry
{
    public MamaStateEntry(ushort agent, bool allowFollow)
    {
        Agent = agent;
        AllowFollow = allowFollow;
    }

    public ushort Agent { get; }
    public bool AllowFollow { get; }
}

public sealed class MamaStateMessage : INetMessage
{
    public MamaStateMessage(IReadOnlyList<MamaStateEntry> mamas)
    {
        Mamas = mamas ?? throw new ArgumentNullException(nameof(mamas));
    }

    public MessageType Type => MessageType.MamaState;
    public IReadOnlyList<MamaStateEntry> Mamas { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Mamas.Count);
        foreach (MamaStateEntry mama in Mamas)
        {
            writer.Write(mama.Agent);
            writer.Write(mama.AllowFollow);
        }
    }

    public static MamaStateMessage Read(BinaryReader reader)
    {
        int count = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxPlayers + 1, "mama state");
        var mamas = new MamaStateEntry[count];
        for (int i = 0; i < count; i++)
        {
            mamas[i] = new MamaStateEntry(reader.ReadUInt16(), reader.ReadBoolean());
        }
        return new MamaStateMessage(mamas);
    }
}
