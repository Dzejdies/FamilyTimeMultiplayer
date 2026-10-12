using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Actions;
using FamilyTimeMultiplayer.Core.Compatibility;
using FamilyTimeMultiplayer.Addons;
using FamilyTimeMultiplayer.Core.Interpolation;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Replication;
using FamilyTimeMultiplayer.Core.Transport;
using FamilyTimeMultiplayer.Game;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.Sessions;

public sealed partial class ClientSession
{
    bool IAddonReplicationEndpoint.IsHost => false;
    GenericEntityRegistry<object> IAddonReplicationEndpoint.Entities => _entityRegistry;
    bool IAddonReplicationEndpoint.IsApplyingRemoteState => _genericStates.IsApplyingRemoteState;
    void IAddonReplicationEndpoint.EntityAvailable(NetworkEntityId identity) => ApplyPendingGenericState(identity);
    void IAddonReplicationEndpoint.ForgetGenericEntityState(NetworkEntityId identity) => _genericStates.ForgetEntity(identity);
    bool IAddonReplicationEndpoint.PublishGenericState(NetworkEntityId identity, object entity, string adapterId) =>
        throw new InvalidOperationException("Only the host may publish component state.");
    void IAddonReplicationEndpoint.RegisterAddonAction(string addonId, IGenericActionAdapter<object> adapter) =>
        throw new InvalidOperationException("Register action execution handlers on the host.");

    void IAddonReplicationEndpoint.RegisterAddonState(string addonId, IEntityComponentAdapter<object> adapter)
    {
        _replicationCapabilities.RegisterState(addonId, adapter.Descriptor);
        RegisterGenericStateAdapter(adapter);
    }

    Guid IAddonReplicationEndpoint.RequestAddonAction(string addonId, string actionId, int schemaVersion,
        NetworkEntityId? target, ulong lifecycleRevision, byte[] payload, Action<GenericActionResultMessage> completed)
    {
        _replicationCapabilities.RequireScopedName(addonId, actionId);
        if (!_sharedAddons.Any(addon => addon.Id == addonId)) throw new InvalidOperationException("Host does not support this optional addon.");
        if (completed == null) throw new ArgumentNullException(nameof(completed));
        SendCurrentPose(GameClock.Now);
        Guid requestId = RequestGenericAction(actionId, schemaVersion, target, lifecycleRevision, payload);
        _addonActionCallbacks.Add(requestId, completed);
        return requestId;
    }

    private void ApplyGenericState(GenericEntityStateMessage state)
    {
        if (!_replicationCapabilities.SupportsState(_sharedAddons, state)) throw new InvalidDataException("Host sent component state for an unnegotiated addon.");
        GenericStateApplyResult result = _genericStates.Apply(state);
        if (result.Status is GenericStateApplyStatus.Rejected or GenericStateApplyStatus.ResyncRequired)
            throw new InvalidOperationException(result.Error);
    }

    private static void ApplyNativeAgentInfo(ushort localIndex, uint info)
    {
        if (localIndex == LocalPlayer.PossessedHandle.stableIndex) return;
        GatInterop.RequireGat().SetInstanceInfo(GatInterop.ToHandle(localIndex), GatInterop.CreateInstanceInfo(AgentInfoBits.ForPuppet(info)));
    }

    public void RegisterGenericStateAdapter(IEntityComponentAdapter<object> adapter) =>
        _genericStates.RegisterAdapter(adapter);

    public IReadOnlyList<GenericStateApplyResult> NotifyGenericEntityAvailable(NetworkEntityId identity) =>
        _genericStates.OnEntityAvailable(identity);

    public event Action<GenericActionResultMessage>? GenericActionCompleted;

    public Guid RequestGenericAction(string actionId, int schemaVersion, NetworkEntityId? target, ulong lifecycleRevision, byte[] payload)
    {
        if (_phase != ClientPhase.Live || _welcome == null || _connection == null) throw new InvalidOperationException("Generic actions require a live session.");
        var request = new GenericActionRequestMessage(actionId, schemaVersion, Guid.NewGuid(), _welcome.WorldGeneration, target, lifecycleRevision, payload);
        _pendingGenericActions.Add(request.RequestId);
        _connection.Send(request);
        return request.RequestId;
    }

}
