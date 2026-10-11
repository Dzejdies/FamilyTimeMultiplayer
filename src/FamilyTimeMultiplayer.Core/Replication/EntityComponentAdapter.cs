using System;
using System.Collections.Generic;

namespace FamilyTimeMultiplayer.Core.Replication;

public sealed class EntityComponentAdapterDescriptor
{
    public EntityComponentAdapterDescriptor(
        string adapterId,
        int schemaVersion,
        EntityStateDomain stateDomain,
        IEnumerable<EntityKind> supportedKinds)
    {
        if (string.IsNullOrWhiteSpace(adapterId)) throw new ArgumentException("Adapter id is required.", nameof(adapterId));
        if (schemaVersion <= 0) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (string.IsNullOrWhiteSpace(stateDomain.Name)) throw new ArgumentException("State domain is required.", nameof(stateDomain));
        if (supportedKinds == null) throw new ArgumentNullException(nameof(supportedKinds));
        var kinds = new HashSet<EntityKind>(supportedKinds);
        if (kinds.Count == 0) throw new ArgumentException("At least one supported entity kind is required.", nameof(supportedKinds));
        AdapterId = adapterId.Trim();
        SchemaVersion = schemaVersion;
        StateDomain = stateDomain;
        SupportedKinds = Array.AsReadOnly(new List<EntityKind>(kinds).ToArray());
    }

    public string AdapterId { get; }
    public int SchemaVersion { get; }
    public EntityStateDomain StateDomain { get; }
    public IReadOnlyList<EntityKind> SupportedKinds { get; }
}

public interface IEntityComponentAdapter<TLocal>
{
    EntityComponentAdapterDescriptor Descriptor { get; }
    byte[] Capture(TLocal entity);
    void Apply(TLocal entity, int schemaVersion, ReadOnlyMemory<byte> state);
}
