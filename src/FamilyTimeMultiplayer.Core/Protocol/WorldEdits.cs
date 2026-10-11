using System;
using System.Collections.Generic;
using System.IO;

namespace FamilyTimeMultiplayer.Core.Protocol;

public enum WorldEditKind : byte
{
    BuildPartCreate = 1,
    BuildPartRemove = 2,
    BuildPlanCreate = 3,
    BuildPlanRemove = 4,
    BuildPlanConstruct = 5,
    PropRemove = 6,
    ItemSpawn = 7,
    ItemDelete = 8,
    TerrainHeight = 9,
    TerrainEffect = 10,
    BuildPartInteract = 11,
    WaterSpawn = 12,
    AgentName = 13,
    Weather = 14,
    Lightning = 15,
    ChopProp = 16,
    ChopRigid = 17,
}

public readonly struct GridKey : IEquatable<GridKey>
{
    public GridKey(int template, int x, int y, int z, byte rotation)
    {
        Template = template;
        X = x;
        Y = y;
        Z = z;
        Rotation = rotation;
    }

    public int Template { get; }
    public int X { get; }
    public int Y { get; }
    public int Z { get; }
    public byte Rotation { get; }

    public bool Equals(GridKey other) => Template == other.Template && X == other.X && Y == other.Y && Z == other.Z && Rotation == other.Rotation;

    public override bool Equals(object? obj) => obj is GridKey other && Equals(other);

    public override int GetHashCode() => (((Template * 397 ^ X) * 397 ^ Y) * 397 ^ Z) * 397 ^ Rotation;

    public override string ToString() => $"part {Template} at ({X},{Y},{Z}) r{Rotation}";

    internal void Write(BinaryWriter writer)
    {
        writer.Write(Template);
        writer.Write(X);
        writer.Write(Y);
        writer.Write(Z);
        writer.Write(Rotation);
    }

    internal static GridKey Read(BinaryReader reader) =>
        new GridKey(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadByte());
}

public sealed class WorldEdit
{
    private WorldEdit(WorldEditKind kind)
    {
        Kind = kind;
    }

    public WorldEditKind Kind { get; }
    public GridKey Grid { get; private set; }
    public bool Transient { get; private set; }
    public float Value { get; private set; }
    public float SecondValue { get; private set; }
    public int Id { get; private set; }
    public int CellX { get; private set; }
    public int CellZ { get; private set; }
    public byte Height { get; private set; }
    public NetVector3 Position { get; private set; }
    public NetQuaternion Rotation { get; private set; }
    public IReadOnlyList<int> Materials { get; private set; } = Array.Empty<int>();
    public string Text { get; private set; } = string.Empty;

    public const int MaxNameLength = 64;

    public static WorldEdit WaterSpawn(int cellX, int cellZ, float level) => new WorldEdit(WorldEditKind.WaterSpawn) { CellX = cellX, CellZ = cellZ, Value = level };

    public static WorldEdit AgentName(ushort hostStableIndex, string name)
    {
        if (name == null || name.Length > MaxNameLength)
        {
            throw new ArgumentException($"A name is required and at most {MaxNameLength} characters long.", nameof(name));
        }
        return new WorldEdit(WorldEditKind.AgentName) { Id = hostStableIndex, Text = name };
    }

    public static WorldEdit Weather(bool raining, bool lightning, bool floods) =>
        new WorldEdit(WorldEditKind.Weather) { Height = (byte)((raining ? 1 : 0) | (lightning ? 2 : 0) | (floods ? 4 : 0)) };

    public bool Raining => (Height & 1) != 0;
    public bool LightningOn => (Height & 2) != 0;
    public bool FloodsOn => (Height & 4) != 0;

    public static WorldEdit Lightning(NetVector3 position) => new WorldEdit(WorldEditKind.Lightning) { Position = position };

    public static WorldEdit BuildPartCreate(GridKey key, bool transient) => new WorldEdit(WorldEditKind.BuildPartCreate) { Grid = key, Transient = transient };

    public static WorldEdit BuildPartRemove(GridKey key) => new WorldEdit(WorldEditKind.BuildPartRemove) { Grid = key };

    public static WorldEdit BuildPartInteract(GridKey key) => new WorldEdit(WorldEditKind.BuildPartInteract) { Grid = key };

    public static WorldEdit BuildPlanCreate(GridKey key, float overlap) => new WorldEdit(WorldEditKind.BuildPlanCreate) { Grid = key, Value = overlap };

    public static WorldEdit BuildPlanRemove(GridKey key) => new WorldEdit(WorldEditKind.BuildPlanRemove) { Grid = key };

    public static WorldEdit BuildPlanConstruct(GridKey key, IReadOnlyList<int> materials, NetVector3 soundPosition)
    {
        if (materials.Count > MaxMaterials)
        {
            throw new ArgumentException($"A construction uses at most {MaxMaterials} materials.", nameof(materials));
        }
        return new WorldEdit(WorldEditKind.BuildPlanConstruct) { Grid = key, Materials = materials, Position = soundPosition };
    }

    public static WorldEdit ChopProp(int cellX, int cellZ) => new WorldEdit(WorldEditKind.ChopProp) { CellX = cellX, CellZ = cellZ };

    public static WorldEdit ChopRigid(int id) => new WorldEdit(WorldEditKind.ChopRigid) { Id = id };

    public static WorldEdit PropRemove(int cellX, int cellZ) => new WorldEdit(WorldEditKind.PropRemove) { CellX = cellX, CellZ = cellZ };

    public static WorldEdit ItemSpawn(int prefabIndex, NetVector3 position, NetQuaternion rotation) =>
        new WorldEdit(WorldEditKind.ItemSpawn) { Id = prefabIndex, Position = position, Rotation = rotation };

    public static WorldEdit ItemDelete(int netId) => new WorldEdit(WorldEditKind.ItemDelete) { Id = netId };

    public static WorldEdit TerrainHeight(int cellX, int cellZ, byte height) => new WorldEdit(WorldEditKind.TerrainHeight) { CellX = cellX, CellZ = cellZ, Height = height };

    public static WorldEdit TerrainEffect(int cellX, int cellZ, float cull, float mask) =>
        new WorldEdit(WorldEditKind.TerrainEffect) { CellX = cellX, CellZ = cellZ, Value = cull, SecondValue = mask };

    public const int MaxMaterials = 64;

    internal void Write(BinaryWriter writer)
    {
        writer.Write((byte)Kind);
        switch (Kind)
        {
            case WorldEditKind.BuildPartCreate:
                Grid.Write(writer);
                writer.Write(Transient);
                break;
            case WorldEditKind.BuildPartRemove:
            case WorldEditKind.BuildPlanRemove:
            case WorldEditKind.BuildPartInteract:
                Grid.Write(writer);
                break;
            case WorldEditKind.BuildPlanCreate:
                Grid.Write(writer);
                writer.Write(Value);
                break;
            case WorldEditKind.BuildPlanConstruct:
                Grid.Write(writer);
                Position.Write(writer);
                writer.Write(Materials.Count);
                foreach (int material in Materials)
                {
                    writer.Write(material);
                }
                break;
            case WorldEditKind.ChopProp:
            case WorldEditKind.PropRemove:
                writer.Write(CellX);
                writer.Write(CellZ);
                break;
            case WorldEditKind.ItemSpawn:
                writer.Write(Id);
                Position.Write(writer);
                Rotation.Write(writer);
                break;
            case WorldEditKind.ChopRigid:
            case WorldEditKind.ItemDelete:
                writer.Write(Id);
                break;
            case WorldEditKind.TerrainHeight:
                writer.Write(CellX);
                writer.Write(CellZ);
                writer.Write(Height);
                break;
            case WorldEditKind.TerrainEffect:
                writer.Write(CellX);
                writer.Write(CellZ);
                writer.Write(Value);
                writer.Write(SecondValue);
                break;
            case WorldEditKind.WaterSpawn:
                writer.Write(CellX);
                writer.Write(CellZ);
                writer.Write(Value);
                break;
            case WorldEditKind.AgentName:
                writer.Write(Id);
                writer.Write(Text);
                break;
            case WorldEditKind.Weather:
                writer.Write(Height);
                break;
            case WorldEditKind.Lightning:
                Position.Write(writer);
                break;
            default:
                throw new InvalidOperationException($"Unknown world edit {Kind}.");
        }
    }

    internal static WorldEdit Read(BinaryReader reader)
    {
        var kind = (WorldEditKind)reader.ReadByte();
        switch (kind)
        {
            case WorldEditKind.BuildPartCreate:
                return BuildPartCreate(GridKey.Read(reader), reader.ReadBoolean());
            case WorldEditKind.BuildPartRemove:
                return BuildPartRemove(GridKey.Read(reader));
            case WorldEditKind.BuildPlanRemove:
                return BuildPlanRemove(GridKey.Read(reader));
            case WorldEditKind.BuildPartInteract:
                return BuildPartInteract(GridKey.Read(reader));
            case WorldEditKind.BuildPlanCreate:
                return BuildPlanCreate(GridKey.Read(reader), reader.ReadSingle());
            case WorldEditKind.BuildPlanConstruct:
            {
                GridKey key = GridKey.Read(reader);
                NetVector3 sound = NetVector3.Read(reader);
                int count = ProtocolGuards.ReadCount(reader, MaxMaterials, "construction materials");
                var materials = new int[count];
                for (int i = 0; i < count; i++)
                {
                    materials[i] = reader.ReadInt32();
                }
                return BuildPlanConstruct(key, materials, sound);
            }
            case WorldEditKind.PropRemove:
                return PropRemove(reader.ReadInt32(), reader.ReadInt32());
            case WorldEditKind.ChopProp:
                return ChopProp(reader.ReadInt32(), reader.ReadInt32());
            case WorldEditKind.ChopRigid:
                return ChopRigid(reader.ReadInt32());
            case WorldEditKind.ItemSpawn:
                return ItemSpawn(reader.ReadInt32(), NetVector3.Read(reader), NetQuaternion.Read(reader));
            case WorldEditKind.ItemDelete:
                return ItemDelete(reader.ReadInt32());
            case WorldEditKind.TerrainHeight:
                return TerrainHeight(reader.ReadInt32(), reader.ReadInt32(), reader.ReadByte());
            case WorldEditKind.TerrainEffect:
                return TerrainEffect(reader.ReadInt32(), reader.ReadInt32(), reader.ReadSingle(), reader.ReadSingle());
            case WorldEditKind.WaterSpawn:
                return WaterSpawn(reader.ReadInt32(), reader.ReadInt32(), reader.ReadSingle());
            case WorldEditKind.AgentName:
            {
                int id = reader.ReadInt32();
                string text = reader.ReadString();
                if (id < 0 || id >= ushort.MaxValue || text.Length > MaxNameLength)
                {
                    throw new InvalidDataException($"Invalid agent name edit for {id}.");
                }
                return AgentName((ushort)id, text);
            }
            case WorldEditKind.Weather:
                return new WorldEdit(WorldEditKind.Weather) { Height = reader.ReadByte() };
            case WorldEditKind.Lightning:
                return Lightning(NetVector3.Read(reader));
            default:
                throw new InvalidDataException($"Unknown world edit kind {(byte)kind}.");
        }
    }
}

public sealed class WorldEditMessage : INetMessage
{
    public const int MaxEdits = 65536;

    public WorldEditMessage(IReadOnlyList<WorldEdit> edits)
    {
        Edits = edits ?? throw new ArgumentNullException(nameof(edits));
        if (edits.Count > MaxEdits)
        {
            throw new ArgumentException($"At most {MaxEdits} world edits fit in one message.", nameof(edits));
        }
    }

    public MessageType Type => MessageType.WorldEdit;
    public IReadOnlyList<WorldEdit> Edits { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Edits.Count);
        foreach (WorldEdit edit in Edits)
        {
            edit.Write(writer);
        }
    }

    public static WorldEditMessage Read(BinaryReader reader)
    {
        int count = ProtocolGuards.ReadCount(reader, MaxEdits, "world edits");
        var edits = new WorldEdit[count];
        for (int i = 0; i < count; i++)
        {
            edits[i] = WorldEdit.Read(reader);
        }
        return new WorldEditMessage(edits);
    }
}
