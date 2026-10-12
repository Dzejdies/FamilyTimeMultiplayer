using System;
using System.Collections.Generic;
using System.Linq;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Replication;

namespace FamilyTimeMultiplayer.Core.Compatibility;

public sealed class ReplicationCapabilities
{
    private readonly HashSet<string> _addons;
    private readonly Dictionary<(EntityKind Kind, EntityStateDomain Domain), string> _stateOwners = new();
    private readonly Dictionary<string, string> _actionOwners = new(StringComparer.Ordinal);

    public ReplicationCapabilities(IEnumerable<AddonDescriptor> addons) =>
        _addons = new HashSet<string>(AddonCapabilities.Normalize(addons).Select(addon => addon.Id), StringComparer.Ordinal);

    public void RegisterState(string addonId, EntityComponentAdapterDescriptor descriptor)
    {
        RequireScopedName(addonId, descriptor.AdapterId);
        RequireScopedName(addonId, descriptor.StateDomain.Name);
        foreach (EntityKind kind in descriptor.SupportedKinds)
            if (_stateOwners.ContainsKey((kind, descriptor.StateDomain)))
                throw new InvalidOperationException("This state domain already has a compatibility owner.");
        foreach (EntityKind kind in descriptor.SupportedKinds) _stateOwners.Add((kind, descriptor.StateDomain), addonId);
    }

    public void RegisterAction(string addonId, GenericActionDescriptor descriptor)
    {
        RequireScopedName(addonId, descriptor.ActionId);
        if (!_actionOwners.TryAdd(descriptor.ActionId, addonId))
            throw new InvalidOperationException("This action already has a compatibility owner.");
    }

    public bool SupportsState(IEnumerable<AddonDescriptor> shared, GenericEntityStateMessage message) =>
        Supports(shared, _stateOwners.TryGetValue((message.Identity.Kind, message.StateDomain), out string? owner) ? owner : null);

    public bool SupportsAction(IEnumerable<AddonDescriptor> shared, string actionId) =>
        Supports(shared, _actionOwners.TryGetValue(actionId, out string? owner) ? owner : null);

    public void RequireScopedName(string addonId, string name)
    {
        if (!_addons.Contains(addonId)) throw new InvalidOperationException("Register the addon before registering its replication adapters.");
        if (!name.StartsWith(addonId + ".", StringComparison.Ordinal))
            throw new ArgumentException($"'{name}' must belong to the '{addonId}.' namespace.", nameof(name));
    }

    private static bool Supports(IEnumerable<AddonDescriptor> shared, string? owner) =>
        owner == null || shared.Any(addon => addon.Id == owner);
}
