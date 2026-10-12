using System;
using System.Collections.Generic;
using System.Linq;

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
        foreach (IEntityComponentAdapter<TLocal> registered in _adapters.Values)
        {
            if (registered.Descriptor.StateDomain != descriptor.StateDomain) continue;
            foreach (EntityKind kind in descriptor.SupportedKinds)
            {
                if (registered.Descriptor.SupportedKinds.Contains(kind))
                    throw new InvalidOperationException($"State domain '{descriptor.StateDomain}' for '{kind}' already has an adapter.");
            }
        }
        _adapters.Add(descriptor.AdapterId, adapter);
        return EntityAdapterRegistrationResult.Registered;
    }

    public IEntityComponentAdapter<TLocal> GetFor(EntityKind kind, EntityStateDomain domain)
    {
        foreach (IEntityComponentAdapter<TLocal> adapter in _adapters.Values)
        {
            EntityComponentAdapterDescriptor descriptor = adapter.Descriptor;
            if (descriptor.StateDomain == domain && descriptor.SupportedKinds.Contains(kind)) return adapter;
        }
        throw new KeyNotFoundException($"No adapter is registered for '{kind}/{domain}'.");
    }

    public IEntityComponentAdapter<TLocal> Get(string adapterId) =>
        _adapters.TryGetValue(adapterId, out IEntityComponentAdapter<TLocal>? adapter)
            ? adapter
            : throw new KeyNotFoundException($"Component adapter '{adapterId}' is not registered.");
}
