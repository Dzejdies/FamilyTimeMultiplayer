using System.Collections.Generic;
using FamilyTimeMultiplayer.Core.Interpolation;
using FamilyTimeMultiplayer.Core.Protocol;
using GAT;
using UnityEngine;
using GatSimulation = global::GAT.GAT;

namespace FamilyTimeMultiplayer.Game;

public sealed class ReplicatedAgentRegistry
{
    private readonly AgentIdentityMap _identities = new AgentIdentityMap();
    private readonly AgentPuppeteer _puppeteer;

    public ReplicatedAgentRegistry(AgentPuppeteer puppeteer)
    {
        _puppeteer = puppeteer;
    }

    public AgentIdentityMap Identities => _identities;

    public int AdoptLoadedWorld()
    {
        _identities.Clear();
        GatSimulation gat = GatInterop.RequireGat();
        for (int physical = 0; physical < gat.instanceCount; physical++)
        {
            Handle handle = gat.GetHandle((ushort)physical);
            if (!handle.isNull)
            {
                _identities.Bind(handle.stableIndex, handle.stableIndex);
            }
        }
        return _identities.Count;
    }

    public int ApplySpawns(AgentSpawnMessage message)
    {
        GatSimulation gat = GatInterop.RequireGat();
        var byTemplate = new Dictionary<int, (List<ushort> HostIndices, List<Matrix4x4> Matrices)>();
        int templateCount = gat.GetTemplateEntries().Count;
        for (int i = 0; i < message.Spawns.Count; i++)
        {
            AgentSpawn spawn = message.Spawns[i];
            if (_identities.ContainsHost(spawn.StableIndex))
            {
                continue;
            }
            if (spawn.TemplateIndex < 0 || spawn.TemplateIndex >= templateCount)
            {
                throw new System.IO.InvalidDataException(
                    $"Host spawned agent {spawn.StableIndex} with template {spawn.TemplateIndex}, but this client has {templateCount} templates.");
            }
            if (!byTemplate.TryGetValue(spawn.TemplateIndex, out var group))
            {
                group = (new List<ushort>(), new List<Matrix4x4>());
                byTemplate.Add(spawn.TemplateIndex, group);
            }
            group.HostIndices.Add(spawn.StableIndex);
            group.Matrices.Add(GatInterop.ToAffineMatrix(spawn.Pose));
        }
        int created = 0;
        using (ReplicationGate.AuthorizeReplication())
        {
            foreach (KeyValuePair<int, (List<ushort> HostIndices, List<Matrix4x4> Matrices)> group in byTemplate)
            {
                Handle[] handles = gat.CreateInstances(group.Value.Matrices.ToArray(), group.Key);
                for (int i = 0; i < handles.Length; i++)
                {
                    _identities.Bind(group.Value.HostIndices[i], handles[i].stableIndex);
                    created++;
                }
            }
        }
        return created;
    }

    public int ApplyDespawns(AgentDespawnMessage message, ushort ownLocalStableIndex)
    {
        var handles = new List<Handle>(message.StableIndices.Count);
        for (int i = 0; i < message.StableIndices.Count; i++)
        {
            if (!_identities.TryGetLocal(message.StableIndices[i], out ushort local) || local == ownLocalStableIndex)
            {
                continue;
            }
            _identities.UnbindHost(message.StableIndices[i], out _);
            _puppeteer.Forget(local);
            handles.Add(GatInterop.ToHandle(local));
        }
        if (handles.Count > 0)
        {
            using (ReplicationGate.AuthorizeReplication())
            {
                GatInterop.RequireGat().DeleteInstances(handles.ToArray());
            }
        }
        return handles.Count;
    }
}
