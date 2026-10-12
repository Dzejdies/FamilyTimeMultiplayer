using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Protocol;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Game.Actions;

[HarmonyPatch]
internal static class NativeFollowInputObserver
{
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.Method(typeof(FollowController), nameof(FollowController.OnFollowInputStarted)),
        AccessTools.Method(typeof(FollowController), nameof(FollowController.OnFollowInputCanceled)),
    };

    private static void Postfix(FollowController __instance, MethodBase __originalMethod, bool __runOriginal)
    {
        if (__runOriginal && NativeCommandForwarding.Owns(__instance, "_follow"))
            ClientCommandBridge.Send(CommandIntentMessage.Follow(__originalMethod.Name == nameof(FollowController.OnFollowInputStarted)));
    }
}

[HarmonyPatch]
internal static class NativeFollowMutationGate
{
    private static readonly HashSet<string> Mutations = new(StringComparer.Ordinal)
    {
        "StartFollowing", "ToggleFollowing", "StopFollowing", "StartFollowingAgents",
        "StartFollowingWolfMama", "StartFollowingAvailableAgents", "StopFollowingAgent",
    };

    private static IEnumerable<MethodBase> TargetMethods() => typeof(FollowController)
        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        .Where(method => method.ReturnType == typeof(void) && Mutations.Contains(method.Name));

    private static bool Prefix(FollowController __instance) => !NativeCommandForwarding.Owns(__instance, "_follow");
}

[HarmonyPatch(typeof(AttackCommandController), nameof(AttackCommandController.TryStartAttackCommand))]
internal static class NativeAttackForwardPatch
{
    private static bool Prefix(AttackCommandController __instance, ref bool __result, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!NativeCommandForwarding.Owns(__instance, "_attack")) return true;
        __result = World.gat != null && World.gatSightBehavior != null &&
            World.gatCommandManager?.GetCommand<GATAttackBehavior>() != null &&
            __instance.TryGetAttackCommandTarget(out Handle target, out Vector3 position) && NativeCommandForwarding.SendAttack(target, position);
        return false;
    }
}

[HarmonyPatch(typeof(AttackCommandController), "ApplyContinuousSelection")]
internal static class NativeAttackSelectionObserver
{
    private static void Prefix(AttackCommandController __instance, Handle targetHandle, Vector3 lastKnownTargetPosHint,
        HashSet<Handle> ____continuousProcessed, bool __runOriginal)
    {
        if (__runOriginal && NativeCommandForwarding.Owns(__instance, "_attack") && !____continuousProcessed.Contains(targetHandle))
            NativeCommandForwarding.SendAttack(targetHandle, lastKnownTargetPosHint);
    }
}

[HarmonyPatch(typeof(AttackCommandController), nameof(AttackCommandController.Rebalance))]
internal static class NativeAttackAuthorityPatch
{
    private static bool Prefix(AttackCommandController __instance) => !NativeCommandForwarding.Owns(__instance, "_attack");
}

[HarmonyPatch]
internal static class NativePickupForwardPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(PickupWorkerScheduler).GetMethods()
        .Where(method => method.Name == nameof(PickupWorkerScheduler.TryTogglePrimaryPickup));

    private static bool Prefix(PickupWorkerScheduler __instance, object[] __args, ref bool __result, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!NativeCommandForwarding.Owns(__instance, "_pickup")) return true;
        __result = ForwardTarget(__args[0]);
        return false;
    }

    private static bool ForwardTarget(object target)
    {
        if (target is RigidTransform rigid) return NativeCommandForwarding.SendRigid(rigid);
        if (target is PropChunkManager.PropCollisionInstance prop && prop.prop != null)
        {
            ClientCommandBridge.Send(CommandIntentMessage.InteractProp(prop.worldX, prop.worldZ, prop.prop.name));
            return true;
        }
        ClientCommandBridge.ShowNotice("Pickup of placed building parts is not supported by the host adapter yet.");
        return false;
    }
}

[HarmonyPatch(typeof(PickupWorkerScheduler), nameof(PickupWorkerScheduler.BeginContinuousSelection))]
internal static class NativePickupSelectionObserver
{
    private static void Postfix(PickupWorkerScheduler __instance, RigidTransform firstRigid, bool __runOriginal)
    {
        if (!__runOriginal || !NativeCommandForwarding.Owns(__instance, "_pickup")) return;
        if (firstRigid != null && ClientCommandBridge.TryGetNetId(firstRigid, out int identity))
            ClientCommandBridge.Send(CommandIntentMessage.PickupSweep(identity, started: true));
    }
}

[HarmonyPatch(typeof(PickupWorkerScheduler), nameof(PickupWorkerScheduler.StopContinuousSelection))]
internal static class NativePickupSelectionEndObserver
{
    private static void Postfix(PickupWorkerScheduler __instance, bool __runOriginal)
    {
        if (__runOriginal && NativeCommandForwarding.Owns(__instance, "_pickup"))
            ClientCommandBridge.Send(CommandIntentMessage.PickupSweep(-1, started: false));
    }
}

[HarmonyPatch(typeof(PickupWorkerScheduler), nameof(PickupWorkerScheduler.TryStartDropoffCommand))]
internal static class NativeDropoffForwardPatch
{
    private static bool Prefix(PickupWorkerScheduler __instance, object[] __args, ref bool __result, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!NativeCommandForwarding.Owns(__instance, "_pickup")) return true;
        ClientCommandBridge.Send(CommandIntentMessage.InteractGround(GatInterop.ToNet((Vector3)__args[1])));
        __result = true;
        return false;
    }
}

[HarmonyPatch]
internal static class NativeConstructionForwardPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(ConstructionScheduler).GetMethods()
        .Where(method => method.Name == "TryStartConstructionCommand");

    private static bool Prefix(ConstructionScheduler __instance, object[] __args, ref bool __result, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!NativeCommandForwarding.Owns(__instance, "_construction")) return true;
        BuildPlanManager.BuildPlan? plan = __args.OfType<BuildPlanManager.BuildPlan>().FirstOrDefault();
        if (plan == null && Camera.main != null)
            plan = GameApiCompatibility.FindCommandPlan(Camera.main.transform, GameApiCompatibility.CommandRaycastDistance(World.commandService, HostPlayerAuthority.HandReach));
        __result = NativeCommandForwarding.SendPlan(plan);
        return false;
    }
}

[HarmonyPatch(typeof(StationCommandController), nameof(StationCommandController.StartNearbyStationCommands))]
internal static class NativeStationForwardPatch
{
    private static bool Prefix(StationCommandController __instance, GATStation __0, ref bool __result, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!NativeCommandForwarding.Owns(__instance, "_station")) return true;
        __result = __0 != null;
        if (__0 != null) ClientCommandBridge.Send(CommandIntentMessage.InteractStation(GatInterop.ToNet(__0.Position)));
        return false;
    }
}

[HarmonyPatch(typeof(CommandOwnershipService), nameof(CommandOwnershipService.SetCommandLocked), typeof(Handle), typeof(bool))]
internal static class NativeCommandLockForwardPatch
{
    private static bool Prefix(Handle __0, ref bool __result, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!ClientCommandBridge.IsActive || !ReplicationGate.BlocksLocalSimulation) return true;
        __result = ClientCommandBridge.TryGetHostAgent(__0.stableIndex, out ushort identity);
        if (__result) ClientCommandBridge.Send(CommandIntentMessage.ToggleCommandLock(identity));
        return false;
    }
}

[HarmonyPatch]
internal static class NativeWorkerAcquisitionGate
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(CommandOwnershipService).GetMethods()
        .Where(method => method.Name == "TryAcquire");

    private static bool Prefix(ref bool __result)
    {
        if (!ReplicationGate.BlocksLocalSimulation) return true;
        __result = false;
        return false;
    }
}
