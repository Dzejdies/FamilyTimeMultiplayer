using System.Collections.Generic;
using FamilyTimeMultiplayer.Core.Interpolation;
using FamilyTimeMultiplayer.Core.Protocol;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.Game;

public sealed class ClientRigidRegistry
{
    private readonly Dictionary<int, RigidTransform> _byNetId = new Dictionary<int, RigidTransform>();
    private readonly Dictionary<RigidTransform, int> _netIdByRigid = new Dictionary<RigidTransform, int>();
    private readonly Dictionary<int, bool> _carried = new Dictionary<int, bool>();
    private readonly HashSet<int> _locallyHeld = new HashSet<int>();
    private readonly SnapshotInterpolator<int> _interpolator;
    private readonly List<InterpolatedEntry<int>> _sampled = new List<InterpolatedEntry<int>>();

    public ClientRigidRegistry(double interpolationDelaySeconds)
    {
        _interpolator = new SnapshotInterpolator<int>(interpolationDelaySeconds);
    }

    public int Count => _byNetId.Count;
    public int BindingVersion { get; private set; }

    public bool TryResolve(int netId, out RigidTransform rigid) =>
        _byNetId.TryGetValue(netId, out rigid) && RigidTransformApi.IsAlive(rigid);

    public void Bind(RigidIndexMapMessage map)
    {
        BindingVersion++;
        _byNetId.Clear();
        _netIdByRigid.Clear();
        _carried.Clear();
        _interpolator.Clear();
        RigidTransformManager manager = HostRigidRegistry.RequireManager();
        if (RigidTransformApi.Count(manager) != map.NetIdsInManagedOrder.Count)
        {
            throw new System.IO.InvalidDataException(
                $"Host world has {map.NetIdsInManagedOrder.Count} items but this client loaded {RigidTransformApi.Count(manager)}.");
        }
        for (int i = 0; i < map.NetIdsInManagedOrder.Count; i++)
        {
            int netId = map.NetIdsInManagedOrder[i];
            if (netId >= 0 && RigidTransformApi.TryGetManagedRigid(manager, i, out RigidTransform rigid))
            {
                Track(netId, rigid);
            }
        }
    }

    public bool TryGetNetId(RigidTransform rigid, out int netId) => _netIdByRigid.TryGetValue(rigid, out netId);

    public void ApplySpawns(RigidSpawnMessage message)
    {
        RigidTransformManager manager = HostRigidRegistry.RequireManager();
        using (ReplicationGate.AuthorizeReplication())
        {
            foreach (RigidSpawn spawn in message.Spawns)
            {
                if (_byNetId.ContainsKey(spawn.NetId))
                {
                    continue;
                }
                if (!RigidTransformApi.TryGetPrefab(manager, spawn.PrefabIndex, out RigidPrefab prefab))
                {
                    throw new System.IO.InvalidDataException($"Host spawned item prefab {spawn.PrefabIndex}, unknown on this client.");
                }
                RigidTransform rigid = RigidTransformApi.Spawn(manager, prefab, GatInterop.ToUnity(spawn.Position), GatInterop.ToUnity(spawn.Rotation));
                if (rigid == null)
                {
                    throw new System.InvalidOperationException($"Spawning replicated item {spawn.NetId} failed.");
                }
                Track(spawn.NetId, rigid);
            }
        }
    }

    public void ApplyDespawns(RigidDespawnMessage message)
    {
        RigidTransformManager manager = HostRigidRegistry.RequireManager();
        using (ReplicationGate.AuthorizeReplication())
        {
            foreach (int netId in message.NetIds)
            {
                if (_byNetId.TryGetValue(netId, out RigidTransform rigid))
                {
                    BindingVersion++;
                    _byNetId.Remove(netId);
                    _netIdByRigid.Remove(rigid);
                    _carried.Remove(netId);
                    _locallyHeld.Remove(netId);
                    if (RigidTransformApi.IsAlive(rigid))
                    {
                        manager.Delete(rigid);
                    }
                }
            }
        }
    }

    public void PushSnapshot(double localTime, RigidSnapshotMessage message)
    {
        var poses = new List<KeyValuePair<int, NetPose>>(message.Rigids.Count);
        foreach (RigidState state in message.Rigids)
        {
            poses.Add(new KeyValuePair<int, NetPose>(state.NetId, new NetPose(state.Position, state.Rotation, NetVector3.One)));
            _carried[state.NetId] = state.Carried;
        }
        _interpolator.Push(localTime, message.HostTimeSeconds, poses);
    }

    public void Apply(double localTime)
    {
        _interpolator.Sample(localTime, _sampled);
        foreach (InterpolatedEntry<int> entry in _sampled)
        {
            if (_locallyHeld.Contains(entry.Key) || !_byNetId.TryGetValue(entry.Key, out RigidTransform rigid) || !RigidTransformApi.IsAlive(rigid))
            {
                continue;
            }
            bool carried = _carried.TryGetValue(entry.Key, out bool value) && value;
            if (rigid.IsCarried != carried)
            {
                rigid.SetCarriedState(carried);
            }
            rigid.SetFrozen(true);
            rigid.SetPosition(GatInterop.ToUnity(entry.Pose.Position));
            rigid.SetRotation(GatInterop.ToUnity(entry.Pose.Rotation));
        }
    }

    public void SetLocallyHeld(int netId, bool held)
    {
        if (held)
        {
            _locallyHeld.Add(netId);
        }
        else
        {
            _locallyHeld.Remove(netId);
        }
    }

    public void ReleaseAll()
    {
        _locallyHeld.Clear();
        foreach (RigidTransform rigid in _byNetId.Values)
        {
            if (RigidTransformApi.IsAlive(rigid) && !rigid.IsCarried)
            {
                rigid.SetFrozen(false);
            }
        }
        BindingVersion++;
        _byNetId.Clear();
        _netIdByRigid.Clear();
        _carried.Clear();
        _interpolator.Clear();
    }

    private void Track(int netId, RigidTransform rigid)
    {
        BindingVersion++;
        _byNetId[netId] = rigid;
        _netIdByRigid[rigid] = netId;
        rigid.SetFrozen(true);
    }
}
