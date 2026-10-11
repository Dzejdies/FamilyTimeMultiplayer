using System;
using System.Collections.Generic;
using System.IO;

namespace FamilyTimeMultiplayer.Core.Protocol;

public enum EditTool : byte { Hand, Mallet, Axe, Shovel, Blueprint }

public sealed class ToolEditsMessage : INetMessage
{
    public const int MaxEdits = 64;
    public ToolEditsMessage(EditTool tool, IReadOnlyList<WorldEdit> edits)
    {
        if (tool > EditTool.Blueprint) throw new ArgumentOutOfRangeException(nameof(tool));
        if (edits == null || edits.Count > MaxEdits) throw new ArgumentException("Too many tool actions.", nameof(edits));
        Tool = tool;
        Edits = new WorldEditMessage(edits);
    }
    public MessageType Type => MessageType.ToolEdits;
    public EditTool Tool { get; }
    public WorldEditMessage Edits { get; }
    public void Write(BinaryWriter writer) { writer.Write((byte)Tool); Edits.Write(writer); }
    public static ToolEditsMessage Read(BinaryReader reader)
    {
        var tool = (EditTool)reader.ReadByte();
        int count = ProtocolGuards.ReadCount(reader, MaxEdits, "tool actions");
        var edits = new WorldEdit[count];
        for (int i = 0; i < count; i++) edits[i] = WorldEdit.Read(reader);
        return new ToolEditsMessage(tool, edits);
    }
}
