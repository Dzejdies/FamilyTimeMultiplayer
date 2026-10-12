using System;
using System.Collections.Generic;
using System.Reflection;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Families;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch]
internal static class FamilyFollowCommanderScopePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(FollowController), nameof(FollowController.RequestFollowFlowField));
        yield return AccessTools.Method(typeof(FollowController), nameof(FollowController.StartFollowingAgents));
        yield return AccessTools.Method(typeof(FollowController), nameof(FollowController.StartFollowingAvailableAgents));
        yield return AccessTools.Method(typeof(FollowController), nameof(FollowController.OnGATInstanceReleased));
    }

    [HarmonyPriority(Priority.First)]
    private static void Prefix(FollowController __instance, out IDisposable? __state) =>
        __state = HostFamilyService.Current != null ? HostCommandHub.Current?.EnterFollowScope(__instance) : null;

    private static void Finalizer(IDisposable? __state) => __state?.Dispose();
}

[HarmonyPatch(typeof(FollowController), nameof(FollowController.StartFollowingAgents))]
internal static class FamilyExplicitFollowersPatch
{
    private static void Prefix(FollowController __instance, ref Handle[] agents)
    {
        HostFamilyService? families = HostFamilyService.Current;
        if (families != null && agents != null)
        {
            agents = families.FilterFollowers(__instance, agents);
        }
    }
}
