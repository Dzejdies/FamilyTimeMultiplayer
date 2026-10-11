using System;
using System.Collections.Generic;
using System.IO;

namespace FamilyTimeMultiplayer.Core.Protocol;

public readonly struct AgentInfoEntry
{
    public AgentInfoEntry(ushort hostStableIndex, uint info)
    {
        HostStableIndex = hostStableIndex;
        Info = info;
    }

    public ushort HostStableIndex { get; }
    public uint Info { get; }
}

public static class AgentInfoBits
{
    public const uint NavigationMask = 0x00000FFF;
    public const uint NoFlowField = 0x000000FF;

    public static uint Visual(uint info) => info & ~NavigationMask;

    public static uint ForPuppet(uint hostInfo) => Visual(hostInfo) | NoFlowField;
}

public sealed class AgentInfoMessage : INetMessage
{
    public AgentInfoMessage(IReadOnlyList<AgentInfoEntry> agents)
    {
        Agents = agents ?? throw new ArgumentNullException(nameof(agents));
    }

    public MessageType Type => MessageType.AgentInfo;
    public IReadOnlyList<AgentInfoEntry> Agents { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Agents.Count);
        foreach (AgentInfoEntry agent in Agents)
        {
            writer.Write(agent.HostStableIndex);
            writer.Write(agent.Info);
        }
    }

    public static AgentInfoMessage Read(BinaryReader reader)
    {
        int count = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxAgents, "agent info");
        var agents = new AgentInfoEntry[count];
        for (int i = 0; i < count; i++)
        {
            agents[i] = new AgentInfoEntry(reader.ReadUInt16(), reader.ReadUInt32());
        }
        return new AgentInfoMessage(agents);
    }
}

public enum AnimationEventKind : byte
{
    Play = 1,
    PlayShared = 2,
    PlayWithinDistance = 3,
    Complete = 4,
    MotionSets = 5,
}

public sealed class AnimationEvent
{
    public AnimationEvent(AnimationEventKind kind, IReadOnlyList<ushort> hostAgents, int primary, int secondary, int motionSetFilter, bool onlyWhenIdling, int onlyAnimationType, bool flag, NetVector3 origin, float radius)
    {
        if (kind < AnimationEventKind.Play || kind > AnimationEventKind.MotionSets)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown animation event kind.");
        }
        Kind = kind;
        HostAgents = hostAgents ?? throw new ArgumentNullException(nameof(hostAgents));
        Primary = primary;
        Secondary = secondary;
        MotionSetFilter = motionSetFilter;
        OnlyWhenIdling = onlyWhenIdling;
        OnlyAnimationType = onlyAnimationType;
        Flag = flag;
        Origin = origin;
        Radius = radius;
    }

    public AnimationEventKind Kind { get; }
    public IReadOnlyList<ushort> HostAgents { get; }
    public int Primary { get; }
    public int Secondary { get; }
    public int MotionSetFilter { get; }
    public bool OnlyWhenIdling { get; }
    public int OnlyAnimationType { get; }
    public bool Flag { get; }
    public NetVector3 Origin { get; }
    public float Radius { get; }

    internal void Write(BinaryWriter writer)
    {
        writer.Write((byte)Kind);
        writer.Write(HostAgents.Count);
        foreach (ushort agent in HostAgents)
        {
            writer.Write(agent);
        }
        writer.Write(Primary);
        writer.Write(Secondary);
        writer.Write(MotionSetFilter);
        writer.Write(OnlyWhenIdling);
        writer.Write(OnlyAnimationType);
        writer.Write(Flag);
        Origin.Write(writer);
        writer.Write(Radius);
    }

    internal static AnimationEvent Read(BinaryReader reader)
    {
        var kind = (AnimationEventKind)reader.ReadByte();
        if (kind < AnimationEventKind.Play || kind > AnimationEventKind.MotionSets)
        {
            throw new InvalidDataException($"Unknown animation event kind {(byte)kind}.");
        }
        int count = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxAgents, "animation agents");
        var agents = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            agents[i] = reader.ReadUInt16();
        }
        return new AnimationEvent(kind, agents, reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadBoolean(), reader.ReadInt32(), reader.ReadBoolean(), NetVector3.Read(reader), reader.ReadSingle());
    }
}

public sealed class AnimationEventsMessage : INetMessage
{
    public const int MaxEvents = 4096;

    public AnimationEventsMessage(IReadOnlyList<AnimationEvent> events)
    {
        Events = events ?? throw new ArgumentNullException(nameof(events));
        if (events.Count > MaxEvents)
        {
            throw new ArgumentException($"At most {MaxEvents} animation events fit in one message.", nameof(events));
        }
    }

    public MessageType Type => MessageType.AnimationEvents;
    public IReadOnlyList<AnimationEvent> Events { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Events.Count);
        foreach (AnimationEvent animation in Events)
        {
            animation.Write(writer);
        }
    }

    public static AnimationEventsMessage Read(BinaryReader reader)
    {
        int count = ProtocolGuards.ReadCount(reader, MaxEvents, "animation events");
        var events = new AnimationEvent[count];
        for (int i = 0; i < count; i++)
        {
            events[i] = AnimationEvent.Read(reader);
        }
        return new AnimationEventsMessage(events);
    }
}
