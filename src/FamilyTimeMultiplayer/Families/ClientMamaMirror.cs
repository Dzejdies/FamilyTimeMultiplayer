using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.Families;

public sealed class ClientMamaMirror
{
    private readonly ManualLogSource _log;
    private readonly Dictionary<ushort, WolfMama> _clonesByAgent = new Dictionary<ushort, WolfMama>();
    private readonly HashSet<ushort> _unbound = new HashSet<ushort>();

    public ClientMamaMirror(ManualLogSource log)
    {
        _log = log;
    }

    public void Apply(MamaRosterMessage roster)
    {
        HashSet<ushort> wanted = roster.Mamas.Select(m => m.AgentStableIndex).ToHashSet();
        foreach (ushort agent in _clonesByAgent.Keys.Where(a => !wanted.Contains(a)).ToArray())
        {
            Remove(agent);
        }
        WolfMama original = MamaCloning.RequireOriginal();
        foreach (MamaEntry entry in roster.Mamas.Where(m => !_clonesByAgent.ContainsKey(m.AgentStableIndex)))
        {
            Vector3 position = World.gat != null && World.fakeGATManager != null && World.fakeGATManager.TryGetPosition(GatInterop.ToHandle(entry.AgentStableIndex), out Vector3 p) ? p : original.transform.position;
            WolfMama clone = MamaCloning.CreateClone(original, position, entry.OwnerName, createAgent: false);
            _clonesByAgent[entry.AgentStableIndex] = clone;
            _unbound.Add(entry.AgentStableIndex);
            _log.LogInfo($"[Mama] mirroring '{entry.OwnerName}'s mama on agent {entry.AgentStableIndex}");
        }
        Tick();
    }

    public void Tick()
    {
        if (_unbound.Count == 0)
        {
            return;
        }
        foreach (ushort agent in _unbound.ToArray())
        {
            if (MamaCloning.TryBindReplicatedAgent(_clonesByAgent[agent], GatInterop.ToHandle(agent)))
            {
                _unbound.Remove(agent);
            }
        }
    }

    public void Clear()
    {
        foreach (ushort agent in _clonesByAgent.Keys.ToArray())
        {
            Remove(agent);
        }
    }

    private void Remove(ushort agent)
    {
        if (_clonesByAgent.TryGetValue(agent, out WolfMama clone) && clone != null)
        {
            MamaCloning.DestroyKeepingAgent(clone);
        }
        _clonesByAgent.Remove(agent);
        _unbound.Remove(agent);
    }
}
