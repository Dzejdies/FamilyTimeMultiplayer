using System;
using System.Collections.Generic;

namespace FamilyTimeMultiplayer.Core.Replication;

public enum EntityAdapterRegistrationResult
{
    Registered,
    AlreadyRegistered,
}

public sealed class EntityComponentAdapterRegistry<TLocal>
{
    private readonly Dictionary<string, IEntityComponentAdapter<TLocal>> _adapters =
        new Dictionary<string, IEntityComponentAdapter<TLocal>>(StringComparer.Ordinal);

    public EntityAdapterRegistrationResult Register(IEntityComponentAdapter<TLocal> adapter)
    {
        if (adapter == null) throw new ArgumentNullException(nameof(adapter));
        EntityComponentAdapterDescriptor descriptor = adapter.Descriptor
            ?? throw new ArgumentException("Adapter descriptor is required.", nameof(adapter));
        if (_adapters.TryGetValue(descriptor.AdapterId, out IEntityComponentAdapter<TLocal>? existing))
        {
            if (ReferenceEquals(existing, adapter)) return EntityAdapterRegistrationResult.AlreadyRegistered;
            throw new InvalidOperationException($"Component adapter '{descriptor.AdapterId}' is already registered.");
        }
        _adapters.Add(descriptor.AdapterId, adapter);
        return EntityAdapterRegistrationResult.Registered;
    }

    public IEntityComponentAdapter<TLocal> Get(string adapterId) =>
        _adapters.TryGetValue(adapterId, out IEntityComponentAdapter<TLocal>? adapter)
            ? adapter
            : throw new KeyNotFoundException($"Component adapter '{adapterId}' is not registered.");
}
