using System.Collections.Generic;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.WorldEdits;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(AxeTool), "ChopCurrentTree")]
internal static class AxeScopePatch
{
    private static bool Prefix(AxeTool __instance, out bool __state, bool __runOriginal)
    {
        __state = false;
        if (!__runOriginal) return false;
        __state = !WorldEditRelay.ClientForwards;
        if (__state) { ToolScope.AxeDepth++; return true; }
        var target = Traverse.Create(__instance).Field<AxeTool.TreeTargetData>("_currentTreeTarget").Value;
        if (target.rigidTransform != null && ClientCommandBridge.TryGetNetId(target.rigidTransform, out int id)) WorldEditRelay.Forward(WorldEdit.ChopRigid(id));
        else if (target.IsValid && target.rigidTransform == null) WorldEditRelay.Forward(WorldEdit.ChopProp(Mathf.FloorToInt(target.treeRootPos.x), Mathf.FloorToInt(target.treeRootPos.z)));
        return false;
    }

    private static void Finalizer(bool __state) { if (__state) ToolScope.AxeDepth--; }
}

[HarmonyPatch(typeof(MalletTool), "UpdateBuildPartRemoval")]
internal static class MalletScopePatch
{
    private static void Prefix() => ToolScope.MalletDepth++;

    private static void Finalizer() => ToolScope.MalletDepth--;
}

[HarmonyPatch(typeof(BuildPartManager), "TryCreateBuildPart")]
internal static class BuildPartCreatePatch
{
    private static bool Prefix(ref bool __result, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!WorldEditRelay.ClientForwards)
        {
            return true;
        }
        __result = false;
        return false;
    }

    private static void Postfix(bool __runOriginal, bool __result, int buildPartTemplateIndex, Vector3Int gridPos, BuildPart.Rotation rotation, bool transient)
    {
        if (__runOriginal && __result)
        {
            WorldEditRelay.Record(WorldEdit.BuildPartCreate(WorldEditRelay.KeyOf(buildPartTemplateIndex, gridPos, rotation), transient));
        }
    }
}

[HarmonyPatch(typeof(BuildPartManager), nameof(BuildPartManager.RemoveBuildPart))]
internal static class BuildPartRemovePatch
{
    private static bool Prefix(int buildPartTemplateIndex, Vector3Int gridPos, BuildPart.Rotation rotation, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!WorldEditRelay.ClientForwards)
        {
            return true;
        }
        if (ToolScope.InMallet)
        {
            WorldEditRelay.Forward(WorldEdit.BuildPartRemove(WorldEditRelay.KeyOf(buildPartTemplateIndex, gridPos, rotation)));
        }
        return false;
    }

    private static void Postfix(bool __runOriginal, int buildPartTemplateIndex, Vector3Int gridPos, BuildPart.Rotation rotation)
    {
        if (__runOriginal)
        {
            WorldEditRelay.Record(WorldEdit.BuildPartRemove(WorldEditRelay.KeyOf(buildPartTemplateIndex, gridPos, rotation)));
        }
    }
}

[HarmonyPatch(typeof(BuildPlanManager), nameof(BuildPlanManager.CreateBuildPlan))]
internal static class BuildPlanCreateForwardPatch
{
    private static bool Prefix(ref bool __result, int templateIndex, Vector3Int gridPosition, BuildPart.Rotation rotation, float overlapVolumePercent, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!WorldEditRelay.ClientForwards)
        {
            return true;
        }
        WorldEditRelay.Forward(WorldEdit.BuildPlanCreate(WorldEditRelay.KeyOf(templateIndex, gridPosition, rotation), overlapVolumePercent));
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(BuildPlanManager), "CreateBuildPlanInternal")]
internal static class BuildPlanCreateRecordPatch
{
    private static void Postfix(bool __result, float overlapVolumePercent, BuildPlanManager.BuildPlan buildPlan, bool __runOriginal)
    {
        if (__runOriginal && __result && buildPlan != null)
        {
            WorldEditRelay.Record(WorldEdit.BuildPlanCreate(WorldEditRelay.KeyOf(buildPlan), overlapVolumePercent));
        }
    }
}

[HarmonyPatch(typeof(BuildPlanManager), nameof(BuildPlanManager.RemoveBuildPlan))]
internal static class BuildPlanRemovePatch
{
    private static bool Prefix(BuildPlanManager __instance, BuildPlanManager.BuildPlan buildPlan, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (buildPlan == null)
        {
            return true;
        }
        if (WorldEditRelay.ClientForwards)
        {
            WorldEditRelay.Forward(WorldEdit.BuildPlanRemove(WorldEditRelay.KeyOf(buildPlan)));
            return false;
        }
        if (__instance.IsBuildPlanActive(buildPlan))
        {
            WorldEditRelay.Record(WorldEdit.BuildPlanRemove(WorldEditRelay.KeyOf(buildPlan)));
        }
        return true;
    }
}

[HarmonyPatch(typeof(BuildPlanManager), nameof(BuildPlanManager.TryConstructBuildPlan))]
internal static class BuildPlanConstructForwardPatch
{
    private static bool Prefix(ref bool __result, BuildPlanManager.BuildPlan buildPlan, IReadOnlyList<RigidTransform> materials, object[] __args, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!WorldEditRelay.ClientForwards || buildPlan == null)
        {
            return true;
        }
        var netIds = new List<int>();
        foreach (RigidTransform material in materials)
        {
            if (material != null && ClientCommandBridge.TryGetNetId(material, out int netId))
            {
                netIds.Add(netId);
            }
        }
        Vector3 soundPosition = __args.Length > 2 && __args[2] is Vector3 legacySoundPosition ? legacySoundPosition : buildPlan.gridPosition;
        WorldEditRelay.Forward(WorldEdit.BuildPlanConstruct(WorldEditRelay.KeyOf(buildPlan), netIds, GatInterop.ToNet(soundPosition)));
        World.buildPlanManager?.PlayConstructionSound(soundPosition);
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Heightfield), nameof(Heightfield.SetRawHeight))]
internal static class TerrainHeightPatch
{
    private static bool Prefix(int worldX, int worldZ, byte height, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (WorldEditRelay.ClientForwards)
        {
            WorldEditRelay.Forward(WorldEdit.TerrainHeight(worldX, worldZ, height));
            return false;
        }
        WorldEditRelay.Record(WorldEdit.TerrainHeight(worldX, worldZ, height));
        return true;
    }
}

[HarmonyPatch(typeof(Heightfield), nameof(Heightfield.SetEffectsTexturePixel))]
internal static class TerrainEffectPatch
{
    private static bool Prefix(int worldX, int worldZ, float cullValue, float maskValue, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (WorldEditRelay.ClientForwards)
        {
            WorldEditRelay.Forward(WorldEdit.TerrainEffect(worldX, worldZ, cullValue, maskValue));
            return false;
        }
        WorldEditRelay.Record(WorldEdit.TerrainEffect(worldX, worldZ, cullValue, maskValue));
        return true;
    }
}

[HarmonyPatch(typeof(DoorLogicManager), nameof(DoorLogicManager.OnPrimary))]
internal static class ClientDoorForwardPatch
{
    private static bool Prefix(LogicData data, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!WorldEditRelay.ClientForwards)
        {
            return true;
        }
        if (data != null && WorldEditRelay.TryKeyOf(data, out GridKey key))
        {
            WorldEditRelay.Forward(WorldEdit.BuildPartInteract(key));
        }
        return false;
    }
}
