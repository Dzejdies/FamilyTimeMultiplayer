using System;
using FamilyTimeMultiplayer.Core.Replication;

namespace FamilyTimeMultiplayer.Game;

public sealed class LegacyRigidIdentityAdapter
{
    public static readonly EntityKind Kind = new EntityKind("rigid");

    private static readonly (EntityStateDomain Domain, ReplicationOwner Owner)[] OwnedDomains =
    {
        (new EntityStateDomain("lifecycle"), new ReplicationOwner("legacy.rigid.lifecycle")),
        (new EntityStateDomain("transform"), new ReplicationOwner("legacy.rigid.snapshots")),
        (new EntityStateDomain("carried"), new ReplicationOwner("legacy.rigid.snapshots")),
    };

    private readonly GenericEntityRegistry<object> _registry;

    public LegacyRigidIdentityAdapter(GenericEntityRegistry<object> registry)
    {
        _registry = registry;
    }

    public NetworkEntityId Bind(int networkId, object localRigid)
    {
        if (networkId < 0) throw new ArgumentOutOfRangeException(nameof(networkId));
        if (localRigid == null) throw new ArgumentNullException(nameof(localRigid));
        return LegacyEntityIdentityBinding.Bind(_registry, Kind, (ulong)networkId, localRigid, OwnedDomains);
    }

    public bool Unbind(int networkId)
    {
        if (networkId < 0) throw new ArgumentOutOfRangeException(nameof(networkId));
        return LegacyEntityIdentityBinding.Unbind(_registry, Kind, (ulong)networkId);
    }
}
