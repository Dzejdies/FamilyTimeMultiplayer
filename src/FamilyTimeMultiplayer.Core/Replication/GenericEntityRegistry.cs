using System;
using System.Collections.Generic;
using System.IO;

namespace FamilyTimeMultiplayer.Core.Replication;

public sealed class GenericEntityRegistry<TLocal> where TLocal : notnull
{
    private sealed class Binding
    {
        public Binding(TLocal local, ulong revision) { Local = local; Revision = revision; }
        public TLocal Local { get; }
        public ulong Revision { get; }
    }

    private readonly Dictionary<NetworkEntityId, Binding> _bindings = new Dictionary<NetworkEntityId, Binding>();
    private readonly Dictionary<TLocal, NetworkEntityId> _identityByLocal;
    private readonly Dictionary<NetworkEntityId, ulong> _lastRevision = new Dictionary<NetworkEntityId, ulong>();
    private readonly Dictionary<(NetworkEntityId Entity, EntityStateDomain Domain), ReplicationOwner> _owners =
        new Dictionary<(NetworkEntityId, EntityStateDomain), ReplicationOwner>();
    private readonly int _creatingThreadId = Environment.CurrentManagedThreadId;

    public GenericEntityRegistry(long worldGeneration, IEqualityComparer<TLocal>? localComparer = null)
    {
        if (worldGeneration <= 0) throw new ArgumentOutOfRangeException(nameof(worldGeneration));
        WorldGeneration = worldGeneration;
        _identityByLocal = new Dictionary<TLocal, NetworkEntityId>(localComparer);
    }

    public long WorldGeneration { get; private set; }

    public int Count
    {
        get { EnsureCreatingThread(); return _bindings.Count; }
    }

    public void ChangeWorldGeneration(long worldGeneration)
    {
        EnsureCreatingThread();
        if (worldGeneration < WorldGeneration) throw new InvalidOperationException("World generation cannot move backwards.");
        if (worldGeneration == WorldGeneration) return;
        _bindings.Clear();
        _identityByLocal.Clear();
        _lastRevision.Clear();
        _owners.Clear();
        WorldGeneration = worldGeneration;
    }

    public EntityLifecycleResult Register(NetworkEntityId identity, TLocal local, ulong revision)
    {
        EnsureIdentity(identity);
        RequireRevision(revision);
        if (_bindings.TryGetValue(identity, out Binding? existing))
        {
            if (existing.Revision == revision && _identityByLocal.Comparer.Equals(existing.Local, local))
                return EntityLifecycleResult.AlreadyApplied;
            if (revision <= existing.Revision) throw Stale(identity, revision);
            throw new InvalidOperationException($"Entity {identity} is already bound at lifecycle revision {existing.Revision}.");
        }
        if (_lastRevision.TryGetValue(identity, out ulong lastRevision) && revision <= lastRevision)
            throw Stale(identity, revision);
        if (_identityByLocal.TryGetValue(local, out NetworkEntityId existingIdentity))
            throw new InvalidOperationException($"Local entity is already bound to {existingIdentity}; refusing to bind it to {identity}.");
        _bindings.Add(identity, new Binding(local, revision));
        _identityByLocal.Add(local, identity);
        _lastRevision[identity] = revision;
        return EntityLifecycleResult.Applied;
    }

    public EntityLifecycleResult Unregister(NetworkEntityId identity, ulong revision)
    {
        EnsureIdentity(identity);
        RequireRevision(revision);
        if (_bindings.TryGetValue(identity, out Binding? binding))
        {
            if (revision <= binding.Revision) throw Stale(identity, revision);
            _bindings.Remove(identity);
            _identityByLocal.Remove(binding.Local);
            RemoveOwners(identity);
            _lastRevision[identity] = revision;
            return EntityLifecycleResult.Applied;
        }
        if (_lastRevision.TryGetValue(identity, out ulong lastRevision))
        {
            if (revision == lastRevision) return EntityLifecycleResult.AlreadyApplied;
            if (revision < lastRevision) throw Stale(identity, revision);
        }
        _lastRevision[identity] = revision;
        return EntityLifecycleResult.Applied;
    }

    public ulong NextLifecycleRevision(NetworkEntityId identity)
    {
        EnsureIdentity(identity);
        ulong previous = _lastRevision.TryGetValue(identity, out ulong revision) ? revision : 0;
        if (previous == ulong.MaxValue) throw new InvalidOperationException($"Lifecycle revision for {identity} is exhausted.");
        return previous + 1;
    }

    public bool TryGetLocal(NetworkEntityId identity, out TLocal local)
    {
        EnsureIdentity(identity);
        if (_bindings.TryGetValue(identity, out Binding? binding))
        {
            local = binding.Local;
            return true;
        }
        local = default!;
        return false;
    }

    public TLocal GetLocal(NetworkEntityId identity)
    {
        if (TryGetLocal(identity, out TLocal local)) return local;
        throw new KeyNotFoundException($"Entity {identity} has no local binding.");
    }

    public bool TryGetIdentity(TLocal local, out NetworkEntityId identity)
    {
        EnsureCreatingThread();
        return _identityByLocal.TryGetValue(local, out identity);
    }

    public void ClaimOwnership(NetworkEntityId identity, EntityStateDomain domain, ReplicationOwner owner)
    {
        EnsureIdentity(identity);
        if (!_bindings.ContainsKey(identity)) throw new InvalidOperationException($"Cannot claim state for unbound entity {identity}.");
        if (string.IsNullOrWhiteSpace(domain.Name)) throw new ArgumentException("State domain is required.", nameof(domain));
        if (string.IsNullOrWhiteSpace(owner.Id)) throw new ArgumentException("Replication owner is required.", nameof(owner));
        var key = (identity, domain);
        if (_owners.TryGetValue(key, out ReplicationOwner existing))
        {
            if (existing == owner) return;
            throw new InvalidOperationException($"State domain '{domain}' for {identity} is already owned by '{existing}'.");
        }
        _owners.Add(key, owner);
    }

    public bool TryGetOwner(NetworkEntityId identity, EntityStateDomain domain, out ReplicationOwner owner)
    {
        EnsureIdentity(identity);
        return _owners.TryGetValue((identity, domain), out owner);
    }

    private void EnsureIdentity(NetworkEntityId identity)
    {
        EnsureCreatingThread();
        if (identity.WorldGeneration != WorldGeneration)
            throw new InvalidOperationException($"Entity {identity} belongs to world generation {identity.WorldGeneration}, current generation is {WorldGeneration}.");
    }

    private void EnsureCreatingThread()
    {
        if (Environment.CurrentManagedThreadId != _creatingThreadId)
            throw new InvalidOperationException("Entity registry operations must run on the thread that created the registry.");
    }

    private static void RequireRevision(ulong revision)
    {
        if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision), "Lifecycle revisions start at one.");
    }

    private static InvalidOperationException Stale(NetworkEntityId identity, ulong revision) =>
        new InvalidOperationException($"Lifecycle revision {revision} for {identity} is stale.");

    private void RemoveOwners(NetworkEntityId identity)
    {
        List<(NetworkEntityId Entity, EntityStateDomain Domain)>? stale = null;
        foreach (var entry in _owners)
        {
            if (entry.Key.Entity == identity) (stale ??= new List<(NetworkEntityId, EntityStateDomain)>()).Add(entry.Key);
        }
        if (stale == null) return;
        foreach (var key in stale) _owners.Remove(key);
    }
}
