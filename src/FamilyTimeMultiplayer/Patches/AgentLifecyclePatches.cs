using System;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;
using UnityEngine;
using GatSimulation = global::GAT.GAT;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(GatSimulation), nameof(GatSimulation.CreateInstance), typeof(Matrix4x4), typeof(int), typeof(Action<Handle>))]
internal static class CreateSingleInstancePatch
{
    private static bool Prefix(ref Handle __result)
    {
        if (!ReplicationGate.BlocksLocalAgentLifecycle)
        {
            return true;
        }
        __result = Handle.Null;
        return false;
    }

    private static void Postfix(bool __runOriginal, Handle __result, Matrix4x4 localToWorldMatrix, int templateIndex)
    {
        if (__runOriginal && !__result.isNull)
        {
            FamilyTimeMultiplayer.Families.HostFamilyService.Current?.OnAgentsCreated(new[] { __result }, new[] { templateIndex });
        }
        if (__runOriginal && ReplicationGate.Role == SessionRole.Host && !__result.isNull)
        {
            ReplicationGate.HostObserver?.OnAgentsCreated(new[] { __result }, new[] { templateIndex }, new[] { localToWorldMatrix });
        }
    }
}

[HarmonyPatch(typeof(GatSimulation), nameof(GatSimulation.CreateInstances), typeof(Matrix4x4[]), typeof(int), typeof(Action<Handle[]>))]
internal static class CreateInstancesSingleTemplatePatch
{
    private static bool Prefix(ref Handle[] __result, Matrix4x4[] localToWorldMatrices)
    {
        if (!ReplicationGate.BlocksLocalAgentLifecycle)
        {
            return true;
        }
        __result = AgentLifecycleBlocking.NullHandles(localToWorldMatrices.Length);
        return false;
    }

    private static void Postfix(bool __runOriginal, Handle[] __result, Matrix4x4[] localToWorldMatrices, int templateIndex)
    {
        if (__runOriginal && __result.Length > 0)
        {
            var templates = new int[__result.Length];
            Array.Fill(templates, templateIndex);
            FamilyTimeMultiplayer.Families.HostFamilyService.Current?.OnAgentsCreated(__result, templates);
            if (ReplicationGate.Role == SessionRole.Host)
            {
                ReplicationGate.HostObserver?.OnAgentsCreated(__result, templates, localToWorldMatrices);
            }
        }
    }
}

[HarmonyPatch(typeof(GatSimulation), nameof(GatSimulation.CreateInstances), typeof(Matrix4x4[]), typeof(int[]), typeof(Action<Handle[]>))]
internal static class CreateInstancesMixedTemplatePatch
{
    private static bool Prefix(ref Handle[] __result, Matrix4x4[] localToWorldMatrices)
    {
        if (!ReplicationGate.BlocksLocalAgentLifecycle)
        {
            return true;
        }
        __result = AgentLifecycleBlocking.NullHandles(localToWorldMatrices.Length);
        return false;
    }

    private static void Postfix(bool __runOriginal, Handle[] __result, Matrix4x4[] localToWorldMatrices, int[] templateIndices)
    {
        if (__runOriginal && __result.Length > 0)
        {
            FamilyTimeMultiplayer.Families.HostFamilyService.Current?.OnAgentsCreated(__result, templateIndices);
        }
        if (__runOriginal && ReplicationGate.Role == SessionRole.Host && __result.Length > 0)
        {
            ReplicationGate.HostObserver?.OnAgentsCreated(__result, templateIndices, localToWorldMatrices);
        }
    }
}

[HarmonyPatch(typeof(GatSimulation), nameof(GatSimulation.DeleteInstances), typeof(Handle[]), typeof(DeletionReason))]
internal static class DeleteInstancesPatch
{
    private static bool Prefix() => !ReplicationGate.BlocksLocalAgentLifecycle;

    private static void Postfix(bool __runOriginal, Handle[] handles)
    {
        if (__runOriginal && handles != null && handles.Length > 0)
        {
            FamilyTimeMultiplayer.Families.HostFamilyService.Current?.OnAgentsDeleted(handles);
        }
        if (__runOriginal && ReplicationGate.Role == SessionRole.Host && handles != null && handles.Length > 0)
        {
            ReplicationGate.HostObserver?.OnAgentsDeleted(handles);
        }
    }
}

internal static class AgentLifecycleBlocking
{
    public static Handle[] NullHandles(int count)
    {
        var handles = new Handle[count];
        Array.Fill(handles, Handle.Null);
        return handles;
    }
}
