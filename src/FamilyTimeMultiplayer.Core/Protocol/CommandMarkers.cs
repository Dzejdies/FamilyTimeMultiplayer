using System;
using System.Collections.Generic;
using System.IO;

namespace FamilyTimeMultiplayer.Core.Protocol;

public enum CommandMarkerKind : byte
{
    Dropoff = 0,
    Goto = 1,
    Construction = 2,
    Attack = 3,
    Pickup = 4,
}

public readonly struct CommandMarker
{
    public CommandMarker(CommandMarkerKind kind, NetVector3 position, ushort hostTarget = ushort.MaxValue)
    {
        if (kind < CommandMarkerKind.Dropoff || kind > CommandMarkerKind.Pickup) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!position.IsFinite) throw new ArgumentException("Command marker position must be finite.", nameof(position));
        if ((kind == CommandMarkerKind.Attack) != (hostTarget != ushort.MaxValue))
            throw new ArgumentException("Only attack markers must identify an agent target.", nameof(hostTarget));
        Kind = kind;
        Position = position;
        HostTarget = hostTarget;
    }

    public CommandMarkerKind Kind { get; }
    public NetVector3 Position { get; }
    public ushort HostTarget { get; }
}

public sealed class CommandMarkersMessage : INetMessage
{
    public CommandMarkersMessage(IReadOnlyList<CommandMarker> markers)
    {
        if (markers == null) throw new ArgumentNullException(nameof(markers));
        if (markers.Count > ProtocolConstants.MaxAgents) throw new ArgumentOutOfRangeException(nameof(markers));
        var snapshot = new CommandMarker[markers.Count];
        for (int i = 0; i < snapshot.Length; i++)
        {
            CommandMarker marker = markers[i];
            snapshot[i] = new CommandMarker(marker.Kind, marker.Position, marker.HostTarget);
        }
        Markers = Array.AsReadOnly(snapshot);
    }

    public MessageType Type => MessageType.CommandMarkers;
    public IReadOnlyList<CommandMarker> Markers { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Markers.Count);
        foreach (CommandMarker marker in Markers)
        {
            writer.Write((byte)marker.Kind);
            writer.Write(marker.HostTarget);
            marker.Position.Write(writer);
        }
    }

    public static CommandMarkersMessage Read(BinaryReader reader)
    {
        int count = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxAgents, "command markers");
        var markers = new CommandMarker[count];
        for (int i = 0; i < count; i++)
        {
            var kind = (CommandMarkerKind)reader.ReadByte();
            ushort target = reader.ReadUInt16();
            NetVector3 position = NetVector3.Read(reader);
            try
            {
                markers[i] = new CommandMarker(kind, position, target);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Invalid command marker.", exception);
            }
        }
        return new CommandMarkersMessage(markers);
    }
}
