using System;

namespace FamilyTimeMultiplayer.Core.Replication;

public readonly struct ReplicationOwner : IEquatable<ReplicationOwner>
{
    public ReplicationOwner(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Replication owner is required.", nameof(id));
        Id = id.Trim();
    }

    public string Id { get; }

    public bool Equals(ReplicationOwner other) => StringComparer.Ordinal.Equals(Id, other.Id);

    public override bool Equals(object? obj) => obj is ReplicationOwner other && Equals(other);

    public override int GetHashCode() => Id == null ? 0 : StringComparer.Ordinal.GetHashCode(Id);

    public override string ToString() => Id ?? string.Empty;

    public static bool operator ==(ReplicationOwner left, ReplicationOwner right) => left.Equals(right);

    public static bool operator !=(ReplicationOwner left, ReplicationOwner right) => !left.Equals(right);
}
