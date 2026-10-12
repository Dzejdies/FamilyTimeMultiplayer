using System;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Replication;

namespace FamilyTimeMultiplayer.Addons;

public interface IReplicatedSessionAddonContext : ISessionAddonContext
{
    IAddonReplicationContext Replication { get; }
}

public interface IAddonReplicationContext
{
    bool IsHost { get; }
    long WorldGeneration { get; }
    bool IsApplyingRemoteState { get; }
    void RegisterStateAdapter(IEntityComponentAdapter<object> adapter);
    void RegisterAction(IGenericActionAdapter<object> adapter);
    bool TryGetIdentity(object entity, out NetworkEntityId identity, out ulong lifecycleRevision);
    bool PublishState(object entity, string adapterId);
    Guid RequestAction(string actionId, int schemaVersion, NetworkEntityId? target, ulong lifecycleRevision,
        byte[] payload, Action<GenericActionResultMessage> completed);
    EntityLifecycleResult BindEntity(EntityKind kind, ulong networkId, object entity, ulong lifecycleRevision);
    EntityLifecycleResult UnbindEntity(EntityKind kind, ulong networkId, ulong lifecycleRevision);
}

internal interface IAddonReplicationEndpoint
{
    bool IsHost { get; }
    GenericEntityRegistry<object> Entities { get; }
    bool IsApplyingRemoteState { get; }
    void RegisterAddonState(string addonId, IEntityComponentAdapter<object> adapter);
    void RegisterAddonAction(string addonId, IGenericActionAdapter<object> adapter);
    bool PublishGenericState(NetworkEntityId identity, object entity, string adapterId);
    Guid RequestAddonAction(string addonId, string actionId, int schemaVersion, NetworkEntityId? target,
        ulong lifecycleRevision, byte[] payload, Action<GenericActionResultMessage> completed);
    void EntityAvailable(NetworkEntityId identity);
    void ForgetGenericEntityState(NetworkEntityId identity);
}

internal sealed class AddonReplicationContext : IAddonReplicationContext
{
    private readonly string _addonId;
    private readonly IAddonReplicationEndpoint _endpoint;
    private readonly Action _requireActive;

    internal AddonReplicationContext(string addonId, IAddonReplicationEndpoint endpoint, Action requireActive)
    {
        _addonId = addonId;
        _endpoint = endpoint;
        _requireActive = requireActive;
    }

    public bool IsHost => _endpoint.IsHost;
    public long WorldGeneration => _endpoint.Entities.WorldGeneration;
    public bool IsApplyingRemoteState => _endpoint.IsApplyingRemoteState;

    public void RegisterStateAdapter(IEntityComponentAdapter<object> adapter)
    {
        _requireActive();
        _endpoint.RegisterAddonState(_addonId, adapter);
    }

    public void RegisterAction(IGenericActionAdapter<object> adapter)
    {
        _requireActive();
        _endpoint.RegisterAddonAction(_addonId, adapter);
    }

    public bool TryGetIdentity(object entity, out NetworkEntityId identity, out ulong lifecycleRevision)
    {
        _requireActive();
        lifecycleRevision = 0;
        return _endpoint.Entities.TryGetIdentity(entity, out identity) &&
            _endpoint.Entities.TryGetLifecycleRevision(identity, out lifecycleRevision);
    }

    public bool PublishState(object entity, string adapterId)
    {
        RequireScopedName(adapterId);
        if (!IsHost) throw new InvalidOperationException("Only the host may publish component state.");
        if (!TryGetIdentity(entity, out NetworkEntityId identity, out _)) return false;
        return _endpoint.PublishGenericState(identity, entity, adapterId);
    }

    public Guid RequestAction(string actionId, int schemaVersion, NetworkEntityId? target, ulong lifecycleRevision,
        byte[] payload, Action<GenericActionResultMessage> completed)
    {
        RequireScopedName(actionId);
        return _endpoint.RequestAddonAction(_addonId, actionId, schemaVersion, target, lifecycleRevision, payload, completed);
    }

    public EntityLifecycleResult BindEntity(EntityKind kind, ulong networkId, object entity, ulong lifecycleRevision)
    {
        RequireScopedName(kind.Name);
        var identity = new NetworkEntityId(WorldGeneration, kind, networkId);
        EntityLifecycleResult result = _endpoint.Entities.Register(identity, entity, lifecycleRevision);
        _endpoint.EntityAvailable(identity);
        return result;
    }

    public EntityLifecycleResult UnbindEntity(EntityKind kind, ulong networkId, ulong lifecycleRevision)
    {
        RequireScopedName(kind.Name);
        var identity = new NetworkEntityId(WorldGeneration, kind, networkId);
        EntityLifecycleResult result = _endpoint.Entities.Unregister(identity, lifecycleRevision);
        _endpoint.ForgetGenericEntityState(identity);
        return result;
    }

    private void RequireScopedName(string name)
    {
        _requireActive();
        if (!name.StartsWith(_addonId + ".", StringComparison.Ordinal))
            throw new ArgumentException("Replication identifiers must belong to this addon.", nameof(name));
    }
}
