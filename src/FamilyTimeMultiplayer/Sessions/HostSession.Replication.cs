using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Actions;
using FamilyTimeMultiplayer.Core.Compatibility;
using FamilyTimeMultiplayer.Addons;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Replication;
using FamilyTimeMultiplayer.Core.Safety;
using FamilyTimeMultiplayer.Core.Transport;
using FamilyTimeMultiplayer.Core.Transport.Relay;
using FamilyTimeMultiplayer.Families;
using FamilyTimeMultiplayer.Game;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.Sessions;

public sealed partial class HostSession
{
    bool IAddonReplicationEndpoint.IsHost => true;
    GenericEntityRegistry<object> IAddonReplicationEndpoint.Entities => _entityRegistry;
    bool IAddonReplicationEndpoint.IsApplyingRemoteState => _genericStates.IsApplyingRemoteState;
    void IAddonReplicationEndpoint.EntityAvailable(NetworkEntityId identity) => _genericStates.OnEntityAvailable(identity);

    void IAddonReplicationEndpoint.RegisterAddonState(string addonId, IEntityComponentAdapter<object> adapter)
    {
        _replicationCapabilities.RegisterState(addonId, adapter.Descriptor);
        RegisterGenericStateAdapter(adapter);
    }

    void IAddonReplicationEndpoint.RegisterAddonAction(string addonId, IGenericActionAdapter<object> adapter)
    {
        _replicationCapabilities.RegisterAction(addonId, adapter.Descriptor);
        RegisterGenericAction(adapter);
    }

    Guid IAddonReplicationEndpoint.RequestAddonAction(string addonId, string actionId, int schemaVersion,
        NetworkEntityId? target, ulong lifecycleRevision, byte[] payload, Action<GenericActionResultMessage> completed) =>
        throw new InvalidOperationException("Host adapters execute their local native actions directly and publish the results.");

    private void PublishAgentInfo(AgentInfoMessage message)
    {
        var states = new List<GenericEntityStateMessage>();
        foreach (AgentInfoEntry entry in message.Agents)
        {
            var identity = new NetworkEntityId(_entityRegistry.WorldGeneration, LegacyAgentIdentityAdapter.Kind, entry.HostStableIndex);
            if (!_entityRegistry.TryGetLocal(identity, out object local)) continue;
            _agentInfoState.SetCapturedInfo((ushort)local, entry.Info);
            GenericEntityStateMessage? state = _genericStates.Publish(identity, local, AgentInfoComponentAdapter.AdapterId);
            if (state != null) states.Add(state);
        }
        foreach (GenericEntityStateBatchMessage batch in GenericEntityStateBatchMessage.CreateBatches(states)) SendWorldEvent(batch);
    }

    public void RegisterGenericStateAdapter(IEntityComponentAdapter<object> adapter) =>
        _genericStates.RegisterAdapter(adapter);

    public void RegisterGenericAction(IGenericActionAdapter<object> adapter) => _genericActions.Register(adapter);

    public bool PublishGenericState(NetworkEntityId identity, object localEntity, string adapterId)
    {
        GenericEntityStateMessage? message = _genericStates.Publish(identity, localEntity, adapterId);
        if (message == null) return false;
        SendWorldEvent(message);
        return true;
    }

    public void ForgetGenericEntityState(NetworkEntityId identity) => _genericStates.ForgetEntity(identity);

    private void ForgetGenericEntityState(EntityKind kind, ulong networkId) =>
        ForgetGenericEntityState(new NetworkEntityId(_entityRegistry.WorldGeneration, kind, networkId));

}
