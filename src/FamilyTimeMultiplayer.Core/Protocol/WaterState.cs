using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyTimeMultiplayer.Core.Protocol;

public sealed class WaterViewMessage : INetMessage
{
    public WaterViewMessage(int x, int z)
    {
        if (x % 32 != 0 || z % 32 != 0 || x < -2304 || z < -2304 || x > 2048 || z > 2048) throw new InvalidDataException("Invalid water view origin.");
        X = x;
        Z = z;
    }
    public int X { get; }
    public int Z { get; }
    public MessageType Type => MessageType.WaterView;
    public void Write(BinaryWriter writer) { writer.Write(X); writer.Write(Z); }
    public static WaterViewMessage Read(BinaryReader reader) => new WaterViewMessage(reader.ReadInt32(), reader.ReadInt32());
}

public sealed class WaterTile
{
    public const int Side = 32;
    public const int Cells = Side * Side;
    private readonly ushort[] _levels;
    public WaterTile(int x, int z, ushort[] levels)
    {
        if (x % Side != 0 || z % Side != 0 || x < -2048 || z < -2048 || x >= 2048 || z >= 2048) throw new InvalidDataException("Invalid water tile origin.");
        if (levels == null || levels.Length != Cells) throw new InvalidDataException("Invalid water tile level count.");
        X = x;
        Z = z;
        _levels = (ushort[])levels.Clone();
    }
    public int X { get; }
    public int Z { get; }
    public ushort LevelAt(int index) => _levels[index];
    public ushort[] CopyLevels() => (ushort[])_levels.Clone();
    internal void Write(BinaryWriter writer)
    {
        writer.Write(X); writer.Write(Z);
        foreach (ushort level in _levels) writer.Write(level);
    }
    internal static WaterTile Read(BinaryReader reader)
    {
        int x = reader.ReadInt32(), z = reader.ReadInt32();
        var levels = new ushort[Cells];
        for (int i = 0; i < Cells; i++) levels[i] = reader.ReadUInt16();
        return new WaterTile(x, z, levels);
    }
}

public sealed class WaterTilesMessage : INetMessage
{
    public const int MaxTiles = 64;
    public WaterTilesMessage(IReadOnlyList<WaterTile> tiles)
    {
        if (tiles == null || tiles.Count > MaxTiles || tiles.Select(tile => (tile.X, tile.Z)).Distinct().Count() != tiles.Count) throw new InvalidDataException("Invalid water tile batch.");
        Tiles = Array.AsReadOnly(tiles.ToArray());
    }
    public MessageType Type => MessageType.WaterTiles;
    public IReadOnlyList<WaterTile> Tiles { get; }
    public void Write(BinaryWriter writer)
    {
        writer.Write(Tiles.Count);
        foreach (WaterTile tile in Tiles) tile.Write(writer);
    }
    public static WaterTilesMessage Read(BinaryReader reader)
    {
        int count = ProtocolGuards.ReadCount(reader, MaxTiles, "water tiles");
        var tiles = new WaterTile[count];
        for (int i = 0; i < count; i++) tiles[i] = WaterTile.Read(reader);
        return new WaterTilesMessage(tiles);
    }
}
