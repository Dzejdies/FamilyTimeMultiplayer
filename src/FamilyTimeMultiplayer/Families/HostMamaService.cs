using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Families;
using FamilyTimeMultiplayer.Core.Protocol;
using GAT;
using UnityEngine;
using GatSimulation = global::GAT.GAT;

namespace FamilyTimeMultiplayer.Families;

public sealed class HostMamaService : ICommanderScopeHook, IDisposable
{
    private const int SpawnRadius = 3;

    private readonly ManualLogSource _log;
    private readonly HostFamilyService _families;
    private readonly WolfMama _original;
    private readonly Dictionary<string, WolfMama> _clonesByOwner = new Dictionary<string, WolfMama>(StringComparer.Ordinal);

    public HostMamaService(ManualLogSource log, HostFamilyService families)
    {
        if (CommanderContext.Hook != null)
        {
            throw new InvalidOperationException("Another commander scope hook is already installed.");
        }
        _log = log;
        _families = families;
        _original = MamaCloning.RequireOriginal();
        CommanderContext.Hook = this;
        Current = this;
    }

    public static HostMamaService? Current { get; private set; }

    public IEnumerable<string> Owners => _clonesByOwner.Where(e => e.Value != null).Select(e => e.Key);

    public ushort? AgentOf(string owner) =>
        _clonesByOwner.TryGetValue(owner, out WolfMama mama) && mama != null && !mama.FakeGATHandle.isNull
            ? mama.FakeGATHandle.stableIndex
            : (ushort?)null;

    public bool TryGetMamaOf(int playerId, out WolfMama mama)
    {
        mama = null!;
        return _families.TryOwnerOfPlayer(playerId, out string owner) && _clonesByOwner.TryGetValue(owner, out mama) && mama != null;
    }

    public bool EnsureMamaFor(string owner, Vector3 near)
    {
        if (_clonesByOwner.TryGetValue(owner, out WolfMama existing) && existing != null)
        {
            return false;
        }
        Vector3 position = GameFamilyAccess.TryFindWalkableNear(near, SpawnRadius, out Vector3 walkable) ? walkable : near;
        WolfMama clone = MamaCloning.CreateClone(_original, position, owner, createAgent: true);
        if (clone.FakeGATHandle.isNull)
        {
            UnityEngine.Object.Destroy(clone.gameObject);
            throw new InvalidOperationException($"The mama clone for '{owner}' did not get a fake GAT agent.");
        }
        _clonesByOwner[owner] = clone;
        _log.LogInfo($"[Mama] '{owner}' got their own mama at {position}, agent {clone.FakeGATHandle}");
        return true;
    }

    public void Restore(FamilyWorldRecord record)
    {
        GatSimulation gat = Game.GatInterop.RequireAlive(World.gat, "World.gat is missing.");
        foreach (FamilyRecord family in record.Families.Where(f => f.MamaAgent.HasValue && !f.IsHostFamily))
        {
            Handle agent = Game.GatInterop.ToHandle(family.MamaAgent!.Value);
            if (!gat.IsHandleValid(agent))
            {
                _log.LogWarning($"[Mama] saved mama agent {agent} of '{family.Owner}' is gone; they will get a new one");
                continue;
            }
            Vector3 position = gat.GetInstanceMatrixImmediate(agent)?.GetColumn(3) ?? _original.transform.position;
            WolfMama clone = MamaCloning.CreateClone(_original, position, family.Owner, createAgent: false);
            if (!MamaCloning.TryBindReplicatedAgent(clone, agent))
            {
                UnityEngine.Object.Destroy(clone.gameObject);
                throw new InvalidOperationException($"Could not bind '{family.Owner}'s saved mama to agent {agent}.");
            }
            _clonesByOwner[family.Owner] = clone;
            _log.LogInfo($"[Mama] restored '{family.Owner}'s mama on saved agent {agent}");
        }
    }

    public MamaRosterMessage CaptureRoster() =>
        new MamaRosterMessage(_clonesByOwner
            .Where(entry => entry.Value != null && !entry.Value.FakeGATHandle.isNull)
            .Select(entry => new MamaEntry(entry.Key, entry.Value.FakeGATHandle.stableIndex))
            .ToArray());

    public void OnEnter(IRemoteCommanderIdentity commander)
    {
        if (TryGetMamaOf(commander.PlayerId, out WolfMama mama))
        {
            MamaCloning.SetActiveInstance(mama);
        }
    }

    public void OnExit(IRemoteCommanderIdentity commander)
    {
        if (_original != null)
        {
            MamaCloning.RestoreOriginal(_original);
        }
    }

    public void ForgetClones()
    {
        foreach (WolfMama clone in _clonesByOwner.Values)
        {
            if (clone != null)
            {
                MamaCloning.DestroyKeepingAgent(clone);
            }
        }
        _clonesByOwner.Clear();
    }

    public void Dispose()
    {
        ForgetClones();
        if (ReferenceEquals(CommanderContext.Hook, this))
        {
            CommanderContext.Hook = null;
        }
        if (Current == this)
        {
            Current = null;
        }
        if (_original != null)
        {
            MamaCloning.RestoreOriginal(_original);
        }
    }
}
