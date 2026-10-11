using System;

namespace FamilyTimeMultiplayer.Core.Replication;

public readonly struct NetworkEntityId : IEquatable<NetworkEntityId>
{
    public NetworkEntityId(long worldGeneration, EntityKind kind, ulong networkId)
    {
        if (worldGeneration <= 0) throw new ArgumentOutOfRangeException(nameof(worldGeneration));
        if (string.IsNullOrWhiteSpace(kind.Name)) throw new ArgumentException("Entity kind is required.", nameof(kind));
        WorldGeneration = worldGeneration;
        Kind = kind;
        NetworkId = networkId;
    }

    public long WorldGeneration { get; }
    public EntityKind Kind { get; }
    public ulong NetworkId { get; }

    public bool Equals(NetworkEntityId other) =>
        WorldGeneration == other.WorldGeneration && Kind == other.Kind && NetworkId == other.NetworkId;

    public override bool Equals(object? obj) => obj is NetworkEntityId other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(WorldGeneration, Kind, NetworkId);

    public override string ToString() => $"{WorldGeneration}/{Kind}/{NetworkId}";

    public static bool operator ==(NetworkEntityId left, NetworkEntityId right) => left.Equals(right);

    public static bool operator !=(NetworkEntityId left, NetworkEntityId right) => !left.Equals(right);
}
