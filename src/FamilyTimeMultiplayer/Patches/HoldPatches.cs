using System;
using FamilyTimeMultiplayer.Holds;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.UI;
using GAT;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(HandService), "ApplyGrabState", typeof(RigidTransform), typeof(Vector3?))]
internal static class ClientHoldBeginPatch
{
    private static void Postfix(RigidTransform rigid, bool __runOriginal)
    {
        if (!__runOriginal) return;
        if (ClientHolds.IsActive && rigid != null)
        {
            ClientHolds.OnGrabbed(rigid);
        }
    }
}

[HarmonyPatch(typeof(HandService), "RestoreAfterDrop", typeof(RigidTransform), typeof(bool))]
internal static class ClientHoldEndPatch
{
    private static void Postfix(RigidTransform rigid)
    {
        if (ClientHolds.IsActive && rigid != null)
        {
            ClientHolds.OnDropped(rigid, FamilyTimeMultiplayer.Game.GameClock.Now);
        }
    }
}

[HarmonyPatch(typeof(HandService), "GrabRigid")]
internal static class HostGrabRemotelyHeldPatch
{
    private static bool Prefix(RigidTransform rigid)
    {
        string? holder = rigid != null ? HostHoldRegistry.Current?.HolderOf(rigid) : null;
        if (holder == null)
        {
            return true;
        }
        PlayerNotice.Show($"{holder} is holding it");
        return false;
    }
}

[HarmonyPatch]
internal static class ClientMamaTogglePatch
{
    private static System.Reflection.MethodBase TargetMethod() => GameApiCompatibility.MamaToggleTargetMethod;

    private static bool Prefix(WolfMama __instance, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!ClientHolds.IsActive)
        {
            return true;
        }
        ClientHolds.ToggleMama(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(CommandService), "OnWolfMamaAllowFollowChanged")]
internal static class HostIgnoresOtherMamasPatch
{
    private static bool Prefix(WolfMama wolfMama)
    {
        if (wolfMama == null || !MamaLookup.IsClone(wolfMama))
        {
            return true;
        }
        wolfMama.RefreshFollowScreenIcon();
        return false;
    }
}

[HarmonyPatch(typeof(HandService), "ApplyGrabState", typeof(Handle))]
internal static class ClientAgentHoldBeginPatch
{
    private static void Postfix(Handle handle, bool __runOriginal)
    {
        if (!__runOriginal) return;
        if (ClientHolds.IsActive)
        {
            ClientHolds.OnAgentGrabbed(handle);
        }
    }
}

[HarmonyPatch(typeof(HandService), "RestoreAfterDrop", typeof(Handle), typeof(bool))]
internal static class ClientAgentHoldEndPatch
{
    private static void Postfix(Handle handle)
    {
        if (ClientHolds.IsActive)
        {
            ClientHolds.OnAgentDropped(handle);
        }
    }
}

[HarmonyPatch(typeof(global::GAT.GAT), nameof(global::GAT.GAT.SetInstanceMatrix), typeof(Handle), typeof(Matrix4x4))]
internal static class ClientAgentHoldMatrixPatch
{
    private static void Postfix(Handle handle, Matrix4x4 matrix, bool __runOriginal)
    {
        if (!__runOriginal) return;
        if (ClientHolds.IsActive && ClientHolds.IsHeldAgent(handle))
        {
            ClientHolds.OnAgentMatrix(handle, matrix);
        }
    }
}

[HarmonyPatch(typeof(global::GAT.GAT), nameof(global::GAT.GAT.SetInstanceVelocity))]
internal static class ClientAgentHoldVelocityPatch
{
    private static void Postfix(Handle handle, Vector3 velocity, bool __runOriginal)
    {
        if (!__runOriginal) return;
        if (ClientHolds.IsActive && ClientHolds.IsHeldAgent(handle))
        {
            ClientHolds.OnAgentVelocity(handle, velocity);
        }
    }
}

[HarmonyPatch(typeof(HandService), "GrabHandle")]
internal static class HostGrabRemotelyHeldAgentPatch
{
    private static bool Prefix(Handle handle)
    {
        string? holder = HostHoldRegistry.Current?.HolderOf(handle);
        if (holder == null)
        {
            return true;
        }
        PlayerNotice.Show($"{holder} is holding them");
        return false;
    }
}

[HarmonyPatch(typeof(AttackCommandController), nameof(AttackCommandController.StopAll))]
internal static class AttackStopsOnlyOwnCommandsPatch
{
    private static readonly System.Reflection.MethodInfo StopCommand =
        AccessTools.Method(typeof(GATAttackBehavior), "StopCommand")
        ?? throw new MissingMethodException(nameof(GATAttackBehavior), "StopCommand");

    private static bool Prefix(List<GATAttackBehavior.AttackCommand> ____sessions, ref bool ____rebalancing)
    {
        if (Commands.HostCommandHub.Current == null)
        {
            return true;
        }
        GATAttackBehavior? behavior = World.gatCommandManager?.GetCommand<GATAttackBehavior>();
        ____rebalancing = true;
        try
        {
            if (behavior != null)
            {
                foreach (GATAttackBehavior.AttackCommand command in ____sessions.ToArray())
                {
                    StopCommand.Invoke(behavior, new object[] { command, true });
                }
            }
            ____sessions.Clear();
        }
        finally
        {
            ____rebalancing = false;
        }
        return false;
    }
}
