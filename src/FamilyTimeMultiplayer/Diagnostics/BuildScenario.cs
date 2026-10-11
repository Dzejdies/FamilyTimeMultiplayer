using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using FamilyTimeMultiplayer.WorldEdits;
using GAT;
using UnityEngine;
using RigidTransform = GAT.RigidTransform;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class BuildScenario : ScenarioRunner
{
    private const string LogPrefabName = "rt_log01";

    private readonly ManualLogSource _log;
    private readonly List<(double At, string Name, bool HostStep, Action Step)> _steps = new List<(double, string, bool, Action)>();
    private double _start = -1;
    private int _next;
    private Vector3Int _origin;
    private int _template = -1;
    private double _nextProbe;

    public BuildScenario(ManualLogSource log)
    {
        _log = log;
        Add(3, "host creates plan A", true, () => CreatePlan(0));
        Add(8, "client creates plan B", false, () => CreatePlan(4));
        Add(13, "client removes plan B", false, () => RemovePlan(4));
        Add(18, "host builds plan A", true, () => World.buildPlanManager!.CompleteBuildPlan(RequirePlan(0)));
        Add(24, "client hammers part A away", false, MalletRemove);
        Add(30, "client digs terrain", false, Dig);
        Add(36, "client chops a log into existence", false, AxeSpawnLog);
        Add(44, "client chops the log away", false, AxeDeleteLog);
        Add(48, "host builds a door", true, BuildDoor);
        Add(54, "client opens the door", false, UseDoor);
        Add(60, "client closes the door", false, UseDoor);
    }

    private void Add(double at, string name, bool host, Action step) => _steps.Add((at, name, host, step));

    public override void Tick(double now, ISession? session)
    {
        if (session == null || !LocalPlayer.IsWorldReady || session.Roster.Count < 2)
        {
            return;
        }
        if (session is ClientSession && !Commands.ClientCommandBridge.IsActive)
        {
            return;
        }
        if (_start < 0)
        {
            _start = now;
            Vector3 anchor = session.Roster.Count > 0 && session.LocalPlayerId == Core.Protocol.PlayerRosterMessage.HostPlayerId
                ? LocalPlayer.Position
                : session.TryGetRemotePlayerPosition(session.Roster.First(r => r.PlayerId == Core.Protocol.PlayerRosterMessage.HostPlayerId), out Vector3 host) ? host : LocalPlayer.Position;
            _origin = new Vector3Int(Mathf.FloorToInt(anchor.x) + 6, Mathf.FloorToInt(anchor.y), Mathf.FloorToInt(anchor.z) + 6);
            _origin = new Vector3Int(0, 64, 0);
            _template = PickTemplate();
            _log.LogInfo($"[Build] timeline as {session.Role}, origin {_origin}, template {_template} '{World.buildPartManager!.BuildPartTemplates[_template].name}'");
        }
        while (_next < _steps.Count && now - _start >= _steps[_next].At)
        {
            (double _, string name, bool hostStep, Action step) = _steps[_next++];
            if (hostStep == (session.Role == SessionRole.Host))
            {
                _log.LogInfo($"[Build] step '{name}'");
                step();
            }
        }
        if (now >= _nextProbe)
        {
            _nextProbe = now + 2;
            Probe();
        }
    }

    private static int PickTemplate()
    {
        IReadOnlyList<BuildPart> templates = World.buildPartManager!.BuildPartTemplates;
        for (int i = 0; i < templates.Count; i++)
        {
            if (templates[i] != null && templates[i].HasMesh && templates[i].rigidTransformPrefab == null)
            {
                return i;
            }
        }
        throw new InvalidOperationException("No mesh build part template without a rigid prefab.");
    }

    private Vector3Int GridAt(int offset) => _origin + new Vector3Int(offset, 0, 0);

    private void CreatePlan(int offset)
    {
        bool created = World.buildPlanManager!.CreateBuildPlan(_template, GridAt(offset), BuildPart.Rotation.R_0, 0.5f);
        _log.LogInfo($"[Build] CreateBuildPlan at {GridAt(offset)} -> {created}");
    }

    private BuildPlanManager.BuildPlan RequirePlan(int offset) =>
        WorldEditRelay.FindPlan(WorldEditRelay.KeyOf(_template, GridAt(offset), BuildPart.Rotation.R_0))
        ?? throw new InvalidOperationException($"No plan at {GridAt(offset)}.");

    private void RemovePlan(int offset) => World.buildPlanManager!.RemoveBuildPlan(RequirePlan(offset));

    private void MalletRemove()
    {
        ToolScope.MalletDepth++;
        try
        {
            World.buildPartManager!.RemoveBuildPart(_template, GridAt(0), BuildPart.Rotation.R_0);
        }
        finally
        {
            ToolScope.MalletDepth--;
        }
    }

    private void Dig()
    {
        Heightfield heightfield = World.heightfield!;
        int x = _origin.x + 10;
        int z = _origin.z;
        byte current = heightfield.GetRawHeight(x, z);
        heightfield.SetRawHeight(x, z, (byte)Math.Max(0, current - 3));
        _log.LogInfo($"[Build] dig ({x},{z}) {current} -> {current - 3}");
    }

    private void AxeSpawnLog()
    {
        RigidTransformManager manager = HostRigidRegistry.RequireManager();
        RigidPrefab prefab = Enumerable.Range(0, manager.PrefabCount)
            .Select(i => RigidTransformApi.TryGetPrefab(manager, i, out RigidPrefab p) ? p : null)
            .First(p => p != null && RigidTransformApi.PrefabName(p) == LogPrefabName)!;
        ToolScope.AxeDepth++;
        try
        {
            RigidTransformApi.Spawn(manager, prefab, (Vector3)_origin + new Vector3(0f, 3f, 8f), Quaternion.identity);
        }
        finally
        {
            ToolScope.AxeDepth--;
        }
    }

    private RigidTransform? LogNearOrigin()
    {
        RigidTransformManager manager = HostRigidRegistry.RequireManager();
        Vector3 at = (Vector3)_origin + new Vector3(0f, 3f, 8f);
        return Enumerable.Range(0, RigidTransformApi.Count(manager))
            .Select(i => RigidTransformApi.TryGetManagedRigid(manager, i, out RigidTransform r) ? r : null)
            .Where(r => r != null && RigidTransformApi.IsAlive(r) && RigidTransformApi.Name(r).StartsWith(LogPrefabName) && Vector3.Distance(r.position, at) < 6f)
            .FirstOrDefault();
    }

    private void AxeDeleteLog()
    {
        RigidTransform log = LogNearOrigin() ?? throw new InvalidOperationException("The chopped log never arrived on the client.");
        ToolScope.AxeDepth++;
        try
        {
            HostRigidRegistry.RequireManager().Delete(log);
        }
        finally
        {
            ToolScope.AxeDepth--;
        }
    }

    private int DoorTemplate()
    {
        IReadOnlyList<BuildPart> templates = World.buildPartManager!.BuildPartTemplates;
        for (int i = 0; i < templates.Count; i++)
        {
            if (templates[i] != null && templates[i].logicManager == LogicManagerEnum.DOOR)
            {
                return i;
            }
        }
        throw new InvalidOperationException("No door build part template.");
    }

    private Vector3Int DoorGrid => GridAt(16);

    private void BuildDoor()
    {
        World.buildPartManager!.CreateBuildPart(DoorTemplate(), DoorGrid, BuildPart.Rotation.R_0);
        _log.LogInfo($"[Build] door template {DoorTemplate()} '{World.buildPartManager.BuildPartTemplates[DoorTemplate()].name}' at {DoorGrid}");
    }

    private void UseDoor()
    {
        string? part = PartAt(DoorGrid);
        BuildPartManager parts = World.buildPartManager!;
        int template = Enumerable.Range(0, parts.BuildPartTemplates.Count).First(i => parts.BuildPartTemplates[i]?.name == part);
        LogicData data = WorldEditRelay.FindLogicData(WorldEditRelay.KeyOf(template, DoorGrid, BuildPart.Rotation.R_0))
            ?? throw new InvalidOperationException($"No usable part at {DoorGrid} ('{part}').");
        parts.Interact(data);
    }

    private static string? PartAt(Vector3Int grid)
    {
        BuildPartManager parts = World.buildPartManager!;
        for (int i = 0; i < parts.BuildPartTemplates.Count; i++)
        {
            if (parts.BuildPartTemplates[i] != null && WorldEditRelay.FindLogicData(WorldEditRelay.KeyOf(i, grid, BuildPart.Rotation.R_0)) != null)
            {
                return parts.BuildPartTemplates[i].name;
            }
        }
        return parts.TryGetBuildPart(grid, BuildPart.Rotation.R_0, out BuildPart part) ? part.name : null;
    }

    private void Probe()
    {
        if (_template < 0)
        {
            return;
        }
        bool planA = WorldEditRelay.FindPlan(WorldEditRelay.KeyOf(_template, GridAt(0), BuildPart.Rotation.R_0)) != null;
        bool planB = WorldEditRelay.FindPlan(WorldEditRelay.KeyOf(_template, GridAt(4), BuildPart.Rotation.R_0)) != null;
        bool partA = World.buildPartManager!.TryGetBuildPart(GridAt(0), BuildPart.Rotation.R_0, out _);
        byte height = World.heightfield!.GetRawHeight(_origin.x + 10, _origin.z);
        _log.LogInfo($"[Check] planA={planA} planB={planB} partA={partA} height={height} logNearOrigin={LogNearOrigin() != null} door='{PartAt(DoorGrid)}'");
    }
}
