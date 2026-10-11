using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Holds;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.WorldEdits;

public static class HostToolAuthority
{
    private static readonly Dictionary<int, double> LastChops = new Dictionary<int, double>();
    private static readonly HashSet<int> PendingShovels = new HashSet<int>();
    private static readonly HashSet<(int, int)> PendingChops = new HashSet<(int, int)>();
    public static void Reset() { LastChops.Clear(); PendingChops.Clear(); PendingShovels.Clear(); }

    public static EditTool ToolFor(WorldEdit edit) => edit.Kind switch
    {
        WorldEditKind.BuildPlanCreate or WorldEditKind.BuildPlanRemove => EditTool.Blueprint,
        WorldEditKind.BuildPartRemove or WorldEditKind.BuildPlanConstruct => EditTool.Mallet,
        WorldEditKind.ChopProp or WorldEditKind.ChopRigid => EditTool.Axe,
        WorldEditKind.TerrainHeight or WorldEditKind.TerrainEffect => EditTool.Shovel,
        WorldEditKind.AgentName or WorldEditKind.BuildPartInteract => EditTool.Hand,
        _ => throw new InvalidDataException($"{edit.Kind} is not a client tool action."),
    };

    public static void Apply(int playerId, string name, ToolEditsMessage request, HostRigidRegistry rigids, ManualLogSource log, Action<Exception> mutationFailed)
    {
        Validate(playerId, request, rigids);
        int? previousPlayer = CurrentPlayerId;
        CurrentPlayerId = playerId;
        try { ApplyValidated(playerId, name, request, rigids, log, mutationFailed); }
        finally { CurrentPlayerId = previousPlayer; }
    }

    public static int? CurrentPlayerId { get; private set; }

    private static void ApplyValidated(int playerId, string name, ToolEditsMessage request, HostRigidRegistry rigids, ManualLogSource log, Action<Exception> mutationFailed)
    {
        if (request.Tool == EditTool.Shovel)
        {
            ApplyShovel(playerId, name, request, rigids, log, mutationFailed);
            return;
        }
        if (request.Tool == EditTool.Axe && request.Edits.Edits.Count > 0)
        {
            if (request.Edits.Edits.Count != 1 || LastChops.TryGetValue(playerId, out double last) && FamilyTimeMultiplayer.Game.GameClock.Now - last < 1)
                throw new InvalidDataException("Axe action exceeds the native chop rate.");
            LastChops[playerId] = FamilyTimeMultiplayer.Game.GameClock.Now;
        }
        foreach (WorldEdit edit in request.Edits.Edits)
        {
            try
            {
                if (edit.Kind == WorldEditKind.ChopProp || edit.Kind == WorldEditKind.ChopRigid)
                    Chop(playerId, edit, rigids, mutationFailed);
                else
                    WorldEditRelay.ApplyValidatedClient(new WorldEditMessage(new[] { edit }), name, rigids, log);
            }
            catch (Exception exception)
            {
                mutationFailed(exception);
                return;
            }
        }
    }

    public static void Validate(int playerId, ToolEditsMessage request, HostRigidRegistry rigids)
    {
        HostPlayerAuthority.RequirePose(playerId);
        if (request.Tool != EditTool.Hand && !World.playerController.Tools.Any(tool => tool != null && (byte)tool.toolEnum == (byte)request.Tool))
            throw new InvalidDataException("Tool is unavailable in the host rules.");
        if (request.Tool == EditTool.Shovel)
        {
            WorldEdit[] corners = request.Edits.Edits.Where(edit => edit.Kind == WorldEditKind.TerrainHeight).ToArray();
            if (corners.Length != 1 || request.Edits.Edits.Count > 5) throw new InvalidDataException("Shovel request must describe one corner and its adjacent surface cells.");
            WorldEdit corner = corners[0];
            foreach (WorldEdit effect in request.Edits.Edits.Where(edit => edit.Kind == WorldEditKind.TerrainEffect))
                if (effect.CellX < corner.CellX - 1 || effect.CellX > corner.CellX || effect.CellZ < corner.CellZ - 1 || effect.CellZ > corner.CellZ)
                    throw new InvalidDataException("Shovel surface effect is not adjacent to the modified corner.");
        }
        var heights = new HashSet<(int, int)>();
        var materials = new HashSet<int>();
        var targets = new HashSet<string>();
        foreach (WorldEdit edit in request.Edits.Edits)
        {
            if (ToolFor(edit) != request.Tool) throw new InvalidDataException("Tool action does not match its authority scope.");
            if (!targets.Add($"{edit.Kind}:{edit.Grid}:{edit.Id}:{edit.CellX}:{edit.CellZ}")) throw new InvalidDataException("Repeated tool target in one request.");
            switch (edit.Kind)
            {
                case WorldEditKind.BuildPlanCreate:
                case WorldEditKind.BuildPlanRemove:
                case WorldEditKind.BuildPartRemove:
                case WorldEditKind.BuildPlanConstruct:
                case WorldEditKind.BuildPartInteract:
                    BuildPart part = RequirePart(edit.Grid);
                    Vector3 point = part.GetPlacementMatrix(new Vector3Int(edit.Grid.X, edit.Grid.Y, edit.Grid.Z), (BuildPart.Rotation)edit.Grid.Rotation).GetColumn(3);
                    float extent = part.GetCombinedMeshBounds().extents.magnitude;
                    float reach = request.Tool == EditTool.Blueprint ? BlueprintReach() : World.playerController.ReachDistance;
                    HostPlayerAuthority.RequireNear(playerId, point, reach + extent + 3);
                    if (edit.Kind == WorldEditKind.BuildPlanCreate)
                    {
                        BlueprintTool blueprint = World.playerController.Tools.OfType<BlueprintTool>().First();
                        float overlap = Traverse.Create(blueprint).Field<float>("overlapVolumePercent").Value;
                        if (part.hideFromBlueprintTool || !float.IsFinite(edit.Value) || Mathf.Abs(overlap - edit.Value) > 0.001f) throw new InvalidDataException("Invalid blueprint template or overlap policy.");
                    }
                    if (edit.Kind == WorldEditKind.BuildPlanConstruct)
                    {
                        if (WorldEditRelay.FindPlan(edit.Grid) == null) throw new InvalidDataException("Unknown construction plan.");
                        HostPlayerAuthority.RequireNear(playerId, GatInterop.ToUnity(edit.Position), reach + extent + 3);
                        foreach (int id in edit.Materials)
                        {
                            if (!materials.Add(id) || !rigids.TryResolve(id, out RigidTransform rigid)) throw new InvalidDataException("Unknown or reused construction material.");
                            if (Vector3.Distance(rigid.position, point) > extent + 6) throw new InvalidDataException("Construction material is too far from its plan.");
                            string? holder = HostHoldRegistry.Current?.HolderOf(rigid);
                            if (holder != null && !HostHoldRegistry.Current!.IsHeldBy(playerId, rigid)) throw new InvalidDataException("Construction material belongs to another hand.");
                            if (PlayerController.hand != null && PlayerController.hand.CollectAllCarriedRigids().Contains(rigid)) throw new InvalidDataException("Construction material is held by the host.");
                        }
                    }
                    if (edit.Kind == WorldEditKind.BuildPartInteract && (part.logicManager != LogicManagerEnum.DOOR || WorldEditRelay.FindLogicData(edit.Grid) == null))
                        throw new InvalidDataException("Unknown door interaction.");
                    break;
                case WorldEditKind.TerrainHeight:
                case WorldEditKind.TerrainEffect:
                    Vector3 cell = RequireCell(edit.CellX, edit.CellZ);
                    HostPlayerAuthority.RequireNear(playerId, cell, World.playerController.ReachDistance + 3);
                    if (edit.Kind == WorldEditKind.TerrainHeight)
                    {
                        if (!heights.Add((edit.CellX, edit.CellZ)) || Math.Abs(edit.Height - World.heightfield.GetRawHeight(edit.CellX, edit.CellZ)) > 1)
                            throw new InvalidDataException("Shovel may only change a cell by one height step.");
                    }
                    else if (edit.Value != 1 || edit.SecondValue != 1) throw new InvalidDataException("Invalid shovel surface effect.");
                    break;
                case WorldEditKind.AgentName:
                    HostPlayerAuthority.RequireOwnedAgent(playerId, (ushort)edit.Id);
                    HostPlayerAuthority.RequireNear(playerId, HostPlayerAuthority.AgentPosition((ushort)edit.Id), HostPlayerAuthority.HandReach);
                    if (HostPlayerAuthority.IsAvatar(GatInterop.ToHandle((ushort)edit.Id))) throw new InvalidDataException("Player avatar names are not editable through agent tools.");
                    break;
                case WorldEditKind.ChopRigid:
                    if (!rigids.TryResolve(edit.Id, out RigidTransform target) || target.axeSpawnGroups == null || target.axeSpawnGroups.Length == 0 || HostHoldRegistry.Current?.HolderOf(target) != null)
                        throw new InvalidDataException("Invalid axe target.");
                    HostPlayerAuthority.RequireRigidNear(playerId, target, World.playerController.ReachDistance + 3);
                    break;
                case WorldEditKind.ChopProp:
                    HostPlayerAuthority.RequireNear(playerId, RequireCell(edit.CellX, edit.CellZ), 6);
                    break;
                default:
                    throw new InvalidDataException("Unsupported client tool action.");
            }
        }
    }

    private static void ApplyShovel(int playerId, string name, ToolEditsMessage request, HostRigidRegistry rigids, ManualLogSource log, Action<Exception> mutationFailed)
    {
        if (!PendingShovels.Add(playerId)) return;
        WorldEdit corner = request.Edits.Edits.Single(edit => edit.Kind == WorldEditKind.TerrainHeight);
        var cells = new List<uint>();
        for (int x = corner.CellX - 1; x <= corner.CellX; x++)
            for (int z = corner.CellZ - 1; z <= corner.CellZ; z++) cells.Add(Cell.Pack(new Vector3Int(x, 0, z)));
        ShovelTool shovel = World.playerController.Tools.OfType<ShovelTool>().First();
        EnvironmentMesh[] trees = Traverse.Create(shovel).Field<EnvironmentMesh[]>("collisionMeshes").Value;
        World.propChunkManager.GetPropCollisionInstances(cells, trees, instances =>
        {
            PendingShovels.Remove(playerId);
            if (!HostPlayerAuthority.Exists(playerId)) return;
            try
            {
                Validate(playerId, request, rigids);
                var blocked = AccessTools.Method(typeof(ShovelTool), "IsCornerBlockedByTree");
                if ((bool)blocked.Invoke(shovel, new object[] { instances, corner.CellX, corner.CellZ })) return;
            }
            catch (InvalidDataException) { return; }
            catch (Exception exception) { mutationFailed(exception); return; }
            try { WorldEditRelay.ApplyValidatedClient(request.Edits, name, rigids, log); }
            catch (Exception exception) { mutationFailed(exception); }
        });
    }

    private static float BlueprintReach() => Traverse.Create(World.playerController.Tools.OfType<BlueprintTool>().First()).Field<float>("buildShowDistance").Value;

    private static BuildPart RequirePart(GridKey key)
    {
        var templates = World.buildPartManager.BuildPartTemplates;
        if (key.Template < 0 || key.Template >= templates.Count || templates[key.Template] == null || key.Rotation > 3)
            throw new InvalidDataException("Invalid build template or rotation.");
        RequireCell(key.X, key.Z);
        if (key.Y < 0 || key.Y > 255) throw new InvalidDataException("Build cell is outside the world.");
        return templates[key.Template];
    }

    private static Vector3 RequireCell(int x, int z)
    {
        if (x < -2048 || z < -2048 || (long)x + 2048 >= World.heightfield.MapWidth || (long)z + 2048 >= World.heightfield.MapDepth)
            throw new InvalidDataException("Tool cell is outside the world.");
        return new Vector3(x, World.heightfield.GetRawHeight(x, z) * 0.5f, z);
    }

    private static void Chop(int playerId, WorldEdit edit, HostRigidRegistry rigids, Action<Exception> mutationFailed)
    {
        AxeTool configured = World.playerController.Tools.OfType<AxeTool>().First();
        if (edit.Kind == WorldEditKind.ChopRigid)
        {
            if (!rigids.TryResolve(edit.Id, out RigidTransform target)) return;
            ExecuteChop(configured, new AxeTool.TreeTargetData { treeRootPos = target.position, closestChopPos = target.position, treeTemplateIndex = -1, rigidTransform = target });
            return;
        }
        EnvironmentMesh[] trees = Traverse.Create(configured).Field<EnvironmentMesh[]>("choppableTrees").Value;
        var cells = new List<uint> { Cell.Pack(new Vector3Int(edit.CellX, 0, edit.CellZ)) };
        if (!PendingChops.Add((edit.CellX, edit.CellZ))) return;
        World.propChunkManager.GetPropCollisionInstances(cells, trees, instances =>
        {
            PendingChops.Remove((edit.CellX, edit.CellZ));
            if (!HostPlayerAuthority.Exists(playerId)) return;
            try
            {
                var tree = instances.FirstOrDefault(candidate => candidate.worldX == edit.CellX && candidate.worldZ == edit.CellZ);
                if (tree.prop == null) return;
                HostPlayerAuthority.RequireNear(playerId, tree.rootPos, 6);
                ExecuteChop(configured, new AxeTool.TreeTargetData { treeRootPos = tree.rootPos, closestChopPos = tree.rootPos, treeTemplateIndex = Array.IndexOf(trees, tree.prop) });
            }
            catch (InvalidDataException) { }
            catch (Exception exception) { mutationFailed(exception); }
        });
    }

    private static void ExecuteChop(AxeTool configured, AxeTool.TreeTargetData target)
    {
        var owner = new GameObject("FTMP authorized axe");
        owner.SetActive(false);
        try
        {
            AxeTool axe = owner.AddComponent<AxeTool>();
            Traverse.Create(axe).Field("choppableTrees").SetValue(Traverse.Create(configured).Field<EnvironmentMesh[]>("choppableTrees").Value);
            Traverse.Create(axe).Field("treeFellAudioGroup").SetValue(Traverse.Create(configured).Field<AudioGroup>("treeFellAudioGroup").Value);
            Traverse.Create(axe).Field("_currentTreeTarget").SetValue(target);
            AccessTools.Method(typeof(AxeTool), "ChopCurrentTree").Invoke(axe, null);
        }
        finally { UnityEngine.Object.Destroy(owner); }
    }
}
