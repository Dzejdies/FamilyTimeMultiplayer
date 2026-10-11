using System;

namespace FamilyTimeMultiplayer.Core.Replication;

public readonly struct EntityKind : IEquatable<EntityKind>
{
    public EntityKind(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Entity kind is required.", nameof(name));
        Name = name.Trim();
    }

    public string Name { get; }

    public bool Equals(EntityKind other) => StringComparer.Ordinal.Equals(Name, other.Name);

    public override bool Equals(object? obj) => obj is EntityKind other && Equals(other);

    public override int GetHashCode() => Name == null ? 0 : StringComparer.Ordinal.GetHashCode(Name);

    public override string ToString() => Name ?? string.Empty;

    public static bool operator ==(EntityKind left, EntityKind right) => left.Equals(right);

    public static bool operator !=(EntityKind left, EntityKind right) => !left.Equals(right);
}
