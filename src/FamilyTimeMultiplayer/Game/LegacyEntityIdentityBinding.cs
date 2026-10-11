using System;
using System.Collections.Generic;
using FamilyTimeMultiplayer.Core.Replication;

namespace FamilyTimeMultiplayer.Game;

internal static class LegacyEntityIdentityBinding
{
    public static NetworkEntityId Bind(
        GenericEntityRegistry<object> registry,
        EntityKind kind,
        ulong networkId,
        object local,
        IReadOnlyList<(EntityStateDomain Domain, ReplicationOwner Owner)> ownedDomains)
    {
        var identity = new NetworkEntityId(registry.WorldGeneration, kind, networkId);
        bool alreadyBound = registry.TryGetLocal(identity, out object existing);
        if (alreadyBound && !Equals(existing, local))
        {
            throw new InvalidOperationException($"Legacy entity {identity} is already bound to a different local object.");
        }
        if (!alreadyBound)
        {
            registry.Register(identity, local, registry.NextLifecycleRevision(identity));
        }
        foreach ((EntityStateDomain domain, ReplicationOwner owner) in ownedDomains)
        {
            registry.ClaimOwnership(identity, domain, owner);
        }
        return identity;
    }

    public static bool Unbind(GenericEntityRegistry<object> registry, EntityKind kind, ulong networkId)
    {
        var identity = new NetworkEntityId(registry.WorldGeneration, kind, networkId);
        if (!registry.TryGetLocal(identity, out _)) return false;
        return registry.Unregister(identity, registry.NextLifecycleRevision(identity)) == EntityLifecycleResult.Applied;
    }
}
