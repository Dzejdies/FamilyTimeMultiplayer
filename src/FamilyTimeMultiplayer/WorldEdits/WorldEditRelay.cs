using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Interpolation;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;
using Unity.Mathematics;
using UnityEngine;
using RigidTransform = GAT.RigidTransform;

namespace FamilyTimeMultiplayer.WorldEdits;

public static class WorldEditRelay
{
    private static readonly List<WorldEdit> HostPending = new List<WorldEdit>();
    private static readonly List<WorldEdit> ClientPending = new List<WorldEdit>();

    private static readonly MethodInfo TryCreateBuildPart = FindTryCreateBuildPart();

    private static readonly AccessTools.FieldRef<BuildPlanManager, List<BuildPlanManager.BuildPlan>> ActivePlans =
        AccessTools.FieldRefAccess<BuildPlanManager, List<BuildPlanManager.BuildPlan>>("activeBuildPlans");

    public static bool ClientForwards => ReplicationGate.BlocksLocalAgentLifecycle && Commands.ClientCommandBridge.IsActive;

    public static bool HostRecords =>
        ReplicationGate.Role == SessionRole.Host && !(World.worldSerializer != null && World.worldSerializer.isLoadingInProgress);

    public static void Record(WorldEdit edit)
    {
        if (HostRecords)
        {
            HostPending.Add(edit);
        }
    }

    public static void Forward(WorldEdit edit) => ClientPending.Add(edit);

    public static IReadOnlyList<WorldEditMessage> TakeHostBatches() => TakeBatches(HostPending);

    public static IReadOnlyList<ToolEditsMessage> TakeClientBatches()
    {
        var batches = new List<ToolEditsMessage>();
        foreach (var group in ClientPending.GroupBy(HostToolAuthority.ToolFor))
        {
            WorldEdit[] edits = group.ToArray();
            for (int i = 0; i < edits.Length; i += ToolEditsMessage.MaxEdits)
                batches.Add(new ToolEditsMessage(group.Key, edits.Skip(i).Take(ToolEditsMessage.MaxEdits).ToArray()));
        }
        ClientPending.Clear();
        return batches;
    }

    public static void Reset()
    {
        HostPending.Clear();
        ClientPending.Clear();
        HostToolAuthority.Reset();
    }

    private static IReadOnlyList<WorldEditMessage> TakeBatches(List<WorldEdit> pending)
    {
        if (pending.Count == 0)
        {
            return Array.Empty<WorldEditMessage>();
        }
        var batches = new List<WorldEditMessage>();
        for (int offset = 0; offset < pending.Count; offset += WorldEditMessage.MaxEdits)
        {
            batches.Add(new WorldEditMessage(pending.Skip(offset).Take(WorldEditMessage.MaxEdits).ToArray()));
        }
        pending.Clear();
        return batches;
    }

    public static GridKey KeyOf(int template, Vector3Int grid, BuildPart.Rotation rotation) =>
        new GridKey(template, grid.x, grid.y, grid.z, (byte)rotation);

    public static GridKey KeyOf(BuildPlanManager.BuildPlan plan) => KeyOf(plan.templateIndex, plan.gridPosition, plan.rotation);

    private static Vector3Int Grid(GridKey key) => new Vector3Int(key.X, key.Y, key.Z);

    public static BuildPlanManager.BuildPlan? FindPlan(GridKey key)
    {
        BuildPlanManager? plans = World.buildPlanManager;
        return plans == null ? null : ActivePlans(plans).FirstOrDefault(p => KeyOf(p).Equals(key));
    }

    public static void ApplyFromHost(WorldEditMessage message, AgentIdentityMap identities)
    {
        using (ReplicationGate.AuthorizeReplication())
        {
            foreach (WorldEdit edit in message.Edits)
            {
                ApplyShared(edit, identities);
            }
        }
    }

    internal static void ApplyValidatedClient(WorldEditMessage message, string playerName, HostRigidRegistry rigids, ManualLogSource log)
    {
        foreach (WorldEdit edit in message.Edits)
        {
            switch (edit.Kind)
            {
                case WorldEditKind.BuildPlanConstruct:
                    Construct(edit, rigids, playerName, log);
                    break;
                case WorldEditKind.BuildPartInteract:
                    Interact(edit.Grid, playerName, log);
                    break;
                case WorldEditKind.ChopProp:
                case WorldEditKind.ChopRigid:
                    throw new System.IO.InvalidDataException("Chops must execute through the native axe.");
                case WorldEditKind.PropRemove:
                    GatInterop.RequireAlive(World.propChunkManager, "World.propChunkManager is missing.").RemoveProp(new uint2((uint)edit.CellX, (uint)edit.CellZ));
                    break;
                case WorldEditKind.ItemSpawn:
                    RigidTransformManager manager = HostRigidRegistry.RequireManager();
                    if (!RigidTransformApi.TryGetPrefab(manager, edit.Id, out RigidPrefab prefab))
                    {
                        throw new System.IO.InvalidDataException($"'{playerName}' spawned unknown item prefab {edit.Id}.");
                    }
                    RigidTransformApi.Spawn(manager, prefab, GatInterop.ToUnity(edit.Position), GatInterop.ToUnity(edit.Rotation));
                    break;
                case WorldEditKind.ItemDelete:
                    if (rigids.TryResolve(edit.Id, out RigidTransform rigid))
                    {
                        HostRigidRegistry.RequireManager().Delete(rigid);
                    }
                    break;
                case WorldEditKind.Weather:
                case WorldEditKind.Lightning:
                    throw new System.IO.InvalidDataException($"{edit.Kind} is host-authoritative and cannot be requested by a client.");
                default:
                    ApplyShared(edit);
                    break;
            }
            if (edit.Kind != WorldEditKind.TerrainHeight && edit.Kind != WorldEditKind.TerrainEffect)
            {
                log.LogInfo($"[Edit] '{playerName}': {Describe(edit)}");
            }
        }
    }

    private static void ApplyShared(WorldEdit edit, AgentIdentityMap? identities = null)
    {
        switch (edit.Kind)
        {
            case WorldEditKind.BuildPartCreate:
                TryCreateBuildPart.Invoke(RequireParts(), BuildPartCreateArguments(edit));
                break;
            case WorldEditKind.BuildPartRemove:
                RequireParts().RemoveBuildPart(edit.Grid.Template, Grid(edit.Grid), (BuildPart.Rotation)edit.Grid.Rotation);
                break;
            case WorldEditKind.BuildPlanCreate:
                if (FindPlan(edit.Grid) == null)
                {
                    RequirePlans().CreateBuildPlan(edit.Grid.Template, Grid(edit.Grid), (BuildPart.Rotation)edit.Grid.Rotation, edit.Value);
                }
                break;
            case WorldEditKind.BuildPlanRemove:
                BuildPlanManager.BuildPlan? plan = FindPlan(edit.Grid);
                if (plan != null)
                {
                    RequirePlans().RemoveBuildPlan(plan);
                }
                break;
            case WorldEditKind.TerrainHeight:
                GatInterop.RequireAlive(World.heightfield, "World.heightfield is missing.").SetRawHeight(edit.CellX, edit.CellZ, edit.Height);
                break;
            case WorldEditKind.TerrainEffect:
                GatInterop.RequireAlive(World.heightfield, "World.heightfield is missing.").SetEffectsTexturePixel(edit.CellX, edit.CellZ, edit.Value, edit.SecondValue);
                break;
            case WorldEditKind.WaterSpawn:
                GatInterop.RequireAlive(World.waterManager, "World.waterManager is missing.")
                    .SpawnWaterAtCell(new Vector2Int(edit.CellX, edit.CellZ), edit.Value);
                break;
            case WorldEditKind.AgentName:
                ushort local = (ushort)edit.Id;
                if (identities != null && !identities.TryGetLocal((ushort)edit.Id, out local))
                {
                    break;
                }
                GatInterop.RequireAlive(World.gatNames, "World.gatNames is missing.").SetName(GatInterop.ToHandle(local), edit.Text);
                break;
            case WorldEditKind.Weather:
                WeatherStateBridge.Apply(edit);
                break;
            case WorldEditKind.Lightning:
                WeatherStateBridge.PlayLightning(edit.Position);
                break;
            default:
                throw new System.IO.InvalidDataException($"{edit.Kind} cannot be applied here.");
        }
    }

    private static MethodInfo FindTryCreateBuildPart()
    {
        Type[] leadingParameters = { typeof(int), typeof(Vector3Int), typeof(BuildPart.Rotation), typeof(bool) };
        return AccessTools.GetDeclaredMethods(typeof(BuildPartManager)).FirstOrDefault(method =>
        {
            ParameterInfo[] parameters = method.GetParameters();
            return method.Name == "TryCreateBuildPart" && parameters.Length >= leadingParameters.Length
                && leadingParameters.Select((type, index) => parameters[index].ParameterType == type).All(matches => matches)
                && parameters.Skip(leadingParameters.Length).All(parameter => parameter.IsOptional);
        }) ?? throw new MissingMethodException(nameof(BuildPartManager), "TryCreateBuildPart");
    }

    private static object[] BuildPartCreateArguments(WorldEdit edit)
    {
        object[] arguments = new object[TryCreateBuildPart.GetParameters().Length];
        arguments[0] = edit.Grid.Template;
        arguments[1] = Grid(edit.Grid);
        arguments[2] = (BuildPart.Rotation)edit.Grid.Rotation;
        arguments[3] = edit.Transient;
        for (int index = 4; index < arguments.Length; index++) arguments[index] = Type.Missing;
        return arguments;
    }

    private static void Construct(WorldEdit edit, HostRigidRegistry rigids, string playerName, ManualLogSource log)
    {
        BuildPlanManager.BuildPlan? plan = FindPlan(edit.Grid);
        if (plan == null)
        {
            log.LogWarning($"[Edit] '{playerName}' tried to build a plan the host does not have: {edit.Grid}");
            return;
        }
        var materials = new List<RigidTransform>();
        foreach (int netId in edit.Materials)
        {
            if (rigids.TryResolve(netId, out RigidTransform material))
            {
                materials.Add(material);
            }
        }
        bool built = GameApiCompatibility.TryConstructBuildPlan(RequirePlans(), plan, materials, GatInterop.ToUnity(edit.Position));
        log.LogInfo($"[Edit] '{playerName}' construction of {edit.Grid} with {materials.Count} material(s): {(built ? "built" : "missing materials")}");
    }

    private static readonly Type PartKeyType =
        AccessTools.Inner(typeof(BuildPartManager), "PartKey") ?? throw new MissingMemberException(nameof(BuildPartManager), "PartKey");

    public static bool TryKeyOf(LogicData data, out GridKey key)
    {
        key = default;
        BuildPartManager parts = RequireParts();
        if (!parts.TryGetBuildPart(data, out BuildPart part, out Vector3Int grid, out BuildPart.Rotation rotation))
        {
            return false;
        }
        key = KeyOf(parts.GetTemplateIndex(part), grid, rotation);
        return true;
    }

    public static LogicData? FindLogicData(GridKey key)
    {
        BuildPartManager parts = RequireParts();
        object partKey = Activator.CreateInstance(PartKeyType, key.Template, Grid(key), (BuildPart.Rotation)key.Rotation);
        var runtimeIds = (System.Collections.IDictionary)Traverse.Create(parts).Field("m_runtimeIdByKey").GetValue();
        var specialData = (System.Collections.IDictionary)Traverse.Create(parts).Field("m_specialDataByRuntimeId").GetValue();
        if (!runtimeIds.Contains(partKey) || !specialData.Contains(runtimeIds[partKey]!))
        {
            return null;
        }
        return Traverse.Create(specialData[runtimeIds[partKey]!]).Field<LogicData>("logicCustomData").Value;
    }

    private static void Interact(GridKey key, string playerName, ManualLogSource log)
    {
        LogicData? data = FindLogicData(key);
        if (data == null)
        {
            log.LogWarning($"[Edit] '{playerName}' used {key}, which the host does not have or cannot be used");
            return;
        }
        RequireParts().Interact(data);
    }

    private static BuildPartManager RequireParts() => GatInterop.RequireAlive(World.buildPartManager, "World.buildPartManager is missing.");

    private static BuildPlanManager RequirePlans() => GatInterop.RequireAlive(World.buildPlanManager, "World.buildPlanManager is missing.");

    public static string Describe(WorldEdit edit) => edit.Kind switch
    {
        WorldEditKind.PropRemove => $"{edit.Kind} cell ({edit.CellX},{edit.CellZ})",
        WorldEditKind.ItemSpawn => $"{edit.Kind} prefab {edit.Id} at ({edit.Position.X:0.0},{edit.Position.Y:0.0},{edit.Position.Z:0.0})",
        WorldEditKind.ItemDelete => $"{edit.Kind} item {edit.Id}",
        WorldEditKind.WaterSpawn => $"{edit.Kind} cell ({edit.CellX},{edit.CellZ}) level {edit.Value:0.###}",
        WorldEditKind.AgentName => $"{edit.Kind} agent {edit.Id}",
        WorldEditKind.Weather => $"{edit.Kind} rain={edit.Raining} lightning={edit.LightningOn} floods={edit.FloodsOn}",
        WorldEditKind.TerrainHeight or WorldEditKind.TerrainEffect => $"{edit.Kind} cell ({edit.CellX},{edit.CellZ})",
        _ => $"{edit.Kind} {edit.Grid}",
    };
}
