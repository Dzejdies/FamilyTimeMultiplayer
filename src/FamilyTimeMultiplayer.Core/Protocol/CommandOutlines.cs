using System;
using System.Collections.Generic;
using System.IO;

namespace FamilyTimeMultiplayer.Core.Protocol;

public enum CommandOutlineKind : byte
{
    Selected = 0,
    Primary = 1,
    Dropoff = 2,
    Construction = 3,
}

public readonly struct CommandOutline
{
    public CommandOutline(CommandOutlineKind kind, int rigidNetId)
    {
        if (kind < CommandOutlineKind.Selected || kind > CommandOutlineKind.Construction) throw new ArgumentOutOfRangeException(nameof(kind));
        if (rigidNetId < 0) throw new ArgumentOutOfRangeException(nameof(rigidNetId));
        Kind = kind;
        RigidNetId = rigidNetId;
    }

    public CommandOutlineKind Kind { get; }
    public int RigidNetId { get; }
}

public sealed class CommandOutlinesMessage : INetMessage
{
    public CommandOutlinesMessage(IReadOnlyList<CommandOutline> outlines)
    {
        if (outlines == null) throw new ArgumentNullException(nameof(outlines));
        if (outlines.Count > ProtocolConstants.MaxRigids) throw new ArgumentOutOfRangeException(nameof(outlines));
        var seen = new HashSet<(CommandOutlineKind, int)>();
        var snapshot = new CommandOutline[outlines.Count];
        for (int i = 0; i < snapshot.Length; i++)
        {
            CommandOutline entry = outlines[i];
            snapshot[i] = new CommandOutline(entry.Kind, entry.RigidNetId);
            if (!seen.Add((entry.Kind, entry.RigidNetId))) throw new ArgumentException("Duplicate command outline.", nameof(outlines));
        }
        Outlines = Array.AsReadOnly(snapshot);
    }

    public MessageType Type => MessageType.CommandOutlines;
    public IReadOnlyList<CommandOutline> Outlines { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Outlines.Count);
        foreach (CommandOutline entry in Outlines)
        {
            writer.Write((byte)entry.Kind);
            writer.Write(entry.RigidNetId);
        }
    }

    public static CommandOutlinesMessage Read(BinaryReader reader)
    {
        int count = ProtocolGuards.ReadCount(reader, ProtocolConstants.MaxRigids, "command outlines");
        var entries = new CommandOutline[count];
        try
        {
            for (int i = 0; i < count; i++) entries[i] = new CommandOutline((CommandOutlineKind)reader.ReadByte(), reader.ReadInt32());
            return new CommandOutlinesMessage(entries);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Invalid command outlines.", exception);
        }
    }
}
