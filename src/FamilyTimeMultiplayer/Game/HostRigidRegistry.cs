using System.Collections.Generic;
using FamilyTimeMultiplayer.Core.Protocol;
using GAT;

namespace FamilyTimeMultiplayer.Game;

public sealed class HostRigidRegistry
{
    private readonly Dictionary<RigidTransform, int> _netIds = new Dictionary<RigidTransform, int>();
    private readonly List<(RigidTransform Rigid, int NetId)> _destroyed = new List<(RigidTransform, int)>();
    private readonly LegacyRigidIdentityAdapter? _entities;
    private int _nextNetId;

    public HostRigidRegistry() { }

    public HostRigidRegistry(FamilyTimeMultiplayer.Core.Replication.GenericEntityRegistry<object> entities)
    {
        _entities = new LegacyRigidIdentityAdapter(entities);
    }

    public int Count => _netIds.Count;

    public void AdoptCurrentWorld()
    {
        _netIds.Clear();
        RigidTransformManager manager = RequireManager();
        for (int i = 0; i < RigidTransformApi.Count(manager); i++)
        {
            if (RigidTransformApi.TryGetManagedRigid(manager, i, out RigidTransform rigid))
            {
                int netId = _nextNetId++;
                _netIds[rigid] = netId;
                _entities?.Bind(netId, rigid);
            }
        }
    }

    public RigidSpawn Register(RigidTransform rigid)
    {
        if (!_netIds.TryGetValue(rigid, out int netId))
        {
            netId = _nextNetId++;
            _netIds.Add(rigid, netId);
        }
        _entities?.Bind(netId, rigid);
        return new RigidSpawn(netId, RequirePrefabIndex(rigid), GatInterop.ToNet(rigid.position), ToNet(rigid.rotation));
    }

    public bool TryUnregister(RigidTransform rigid, out int netId)
    {
        if (!_netIds.TryGetValue(rigid, out netId))
        {
            return false;
        }
        _netIds.Remove(rigid);
        _entities?.Unbind(netId);
        return true;
    }

    public bool TryGetNetId(RigidTransform rigid, out int netId)
    {
        netId = -1;
        return RigidTransformApi.IsAlive(rigid) && _netIds.TryGetValue(rigid, out netId);
    }

    public bool TryResolve(int netId, out RigidTransform rigid)
    {
        foreach (KeyValuePair<RigidTransform, int> entry in _netIds)
        {
            if (entry.Value == netId && RigidTransformApi.IsAlive(entry.Key))
            {
                rigid = entry.Key;
                return true;
            }
        }
        rigid = null!;
        return false;
    }

    public RigidIndexMapMessage CaptureIndexMap()
    {
        RigidTransformManager manager = RequireManager();
        var ids = new int[RigidTransformApi.Count(manager)];
        for (int i = 0; i < ids.Length; i++)
        {
            ids[i] = RigidTransformApi.TryGetManagedRigid(manager, i, out RigidTransform rigid) ? Register(rigid).NetId : -1;
        }
        return new RigidIndexMapMessage(ids);
    }

    public IReadOnlyList<int> CollectDestroyed()
    {
        List<int>? removed = null;
        _destroyed.Clear();
        foreach (KeyValuePair<RigidTransform, int> entry in _netIds)
        {
            if (!RigidTransformApi.IsAlive(entry.Key))
            {
                _destroyed.Add((entry.Key!, entry.Value));
                removed ??= new List<int>();
                removed.Add(entry.Value);
            }
        }
        foreach ((RigidTransform gone, int netId) in _destroyed)
        {
            _netIds.Remove(gone);
            _entities?.Unbind(netId);
        }
        return removed ?? (IReadOnlyList<int>)System.Array.Empty<int>();
    }

    public RigidSnapshotMessage Capture(double hostTime)
    {
        var states = new List<RigidState>(_netIds.Count);
        foreach (KeyValuePair<RigidTransform, int> entry in _netIds)
        {
            if (RigidTransformApi.IsAlive(entry.Key))
            {
                states.Add(new RigidState(entry.Value, GatInterop.ToNet(entry.Key.position), ToNet(entry.Key.rotation), entry.Key.IsCarried));
            }
        }
        return new RigidSnapshotMessage(hostTime, states);
    }

    private static int RequirePrefabIndex(RigidTransform rigid)
    {
        RigidTransformManager manager = RequireManager();
        if (manager.TryGetManagedIndex(rigid, out int index) && RigidTransformApi.TryGetManagedRigid(manager, index, out _, out int prefabIndex))
        {
            return prefabIndex;
        }
        throw new System.InvalidOperationException($"Rigid '{RigidTransformApi.Name(rigid)}' is not managed by RigidTransformManager; it cannot be replicated.");
    }

    private static NetQuaternion ToNet(UnityEngine.Quaternion rotation) => new NetQuaternion(rotation.x, rotation.y, rotation.z, rotation.w);

    internal static RigidTransformManager RequireManager() =>
        GatInterop.RequireAlive(World.rigidTransformManager, "World.rigidTransformManager is missing.");
}
