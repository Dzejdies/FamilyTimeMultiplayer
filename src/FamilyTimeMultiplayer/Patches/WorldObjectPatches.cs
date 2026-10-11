using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;
using Unity.Mathematics;
using RigidTransform = GAT.RigidTransform;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(RigidTransformManager), nameof(RigidTransformManager.Spawn))]
internal static class RigidSpawnPatch
{
    private static bool Prefix(RigidTransformManager __instance, ref RigidTransform __result, UnityEngine.GameObject prefab, UnityEngine.Vector3 position, UnityEngine.Quaternion rotation)
    {
        if (!ReplicationGate.BlocksLocalAgentLifecycle)
        {
            return true;
        }
        __result = null!;
        return false;
    }

    private static void Postfix(bool __runOriginal, RigidTransform __result)
    {
        if (__runOriginal && __result != null && ReplicationGate.Role == SessionRole.Host)
        {
            ReplicationGate.HostObserver?.OnRigidSpawned(__result);
        }
    }
}

[HarmonyPatch(typeof(RigidTransformManager), nameof(RigidTransformManager.Delete))]
internal static class RigidDeletePatch
{
    private static bool Prefix(RigidTransform rigid)
    {
        if (ReplicationGate.BlocksLocalAgentLifecycle)
        {
            return false;
        }
        if (rigid != null && ReplicationGate.Role == SessionRole.Host)
        {
            ReplicationGate.HostObserver?.OnRigidDeleting(rigid);
        }
        return true;
    }
}

[HarmonyPatch(typeof(PropChunkManager), nameof(PropChunkManager.AddProp))]
internal static class PropAddPatch
{
    private static bool Prefix(uint2 worldXZCell, EnvironmentMesh prop)
    {
        if (ReplicationGate.BlocksLocalAgentLifecycle)
        {
            return false;
        }
        if (prop != null && ReplicationGate.Role == SessionRole.Host)
        {
            ReplicationGate.HostObserver?.OnPropAdded(worldXZCell.x, worldXZCell.y, prop);
        }
        return true;
    }
}

[HarmonyPatch(typeof(PropChunkManager), nameof(PropChunkManager.RemoveProp))]
internal static class PropRemovePatch
{
    private static bool Prefix(uint2 worldXZCell)
    {
        if (ReplicationGate.BlocksLocalAgentLifecycle)
        {
            return false;
        }
        if (ReplicationGate.Role == SessionRole.Host)
        {
            ReplicationGate.HostObserver?.OnPropRemoved(worldXZCell.x, worldXZCell.y);
        }
        return true;
    }
}
