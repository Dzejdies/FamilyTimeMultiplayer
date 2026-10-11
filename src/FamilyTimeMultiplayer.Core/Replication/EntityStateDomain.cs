using System;

namespace FamilyTimeMultiplayer.Core.Replication;

public readonly struct EntityStateDomain : IEquatable<EntityStateDomain>
{
    public EntityStateDomain(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("State domain is required.", nameof(name));
        Name = name.Trim();
    }

    public string Name { get; }

    public bool Equals(EntityStateDomain other) => StringComparer.Ordinal.Equals(Name, other.Name);

    public override bool Equals(object? obj) => obj is EntityStateDomain other && Equals(other);

    public override int GetHashCode() => Name == null ? 0 : StringComparer.Ordinal.GetHashCode(Name);

    public override string ToString() => Name ?? string.Empty;

    public static bool operator ==(EntityStateDomain left, EntityStateDomain right) => left.Equals(right);

    public static bool operator !=(EntityStateDomain left, EntityStateDomain right) => !left.Equals(right);
}
