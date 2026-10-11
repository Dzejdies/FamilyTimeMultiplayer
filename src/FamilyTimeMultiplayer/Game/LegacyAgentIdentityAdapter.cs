using FamilyTimeMultiplayer.Core.Replication;

namespace FamilyTimeMultiplayer.Game;

public sealed class LegacyAgentIdentityAdapter
{
    public static readonly EntityKind Kind = new EntityKind("agent");

    private static readonly (EntityStateDomain Domain, ReplicationOwner Owner)[] OwnedDomains =
    {
        (new EntityStateDomain("lifecycle"), new ReplicationOwner("legacy.agent.lifecycle")),
        (new EntityStateDomain("transform"), new ReplicationOwner("legacy.agent.snapshots")),
        (new EntityStateDomain("family"), new ReplicationOwner("legacy.family-service")),
        (new EntityStateDomain("appearance"), new ReplicationOwner("legacy.agent-info")),
        (new EntityStateDomain("animation"), new ReplicationOwner("legacy.animation-relay")),
        (new EntityStateDomain("name"), new ReplicationOwner("legacy.world-edits")),
    };

    private readonly GenericEntityRegistry<object> _registry;

    public LegacyAgentIdentityAdapter(GenericEntityRegistry<object> registry)
    {
        _registry = registry;
    }

    public NetworkEntityId Bind(ushort networkStableIndex, ushort localStableIndex) =>
        LegacyEntityIdentityBinding.Bind(_registry, Kind, networkStableIndex, localStableIndex, OwnedDomains);

    public bool Unbind(ushort networkStableIndex) =>
        LegacyEntityIdentityBinding.Unbind(_registry, Kind, networkStableIndex);
}
