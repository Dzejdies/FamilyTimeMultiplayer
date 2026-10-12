using System;
using System.Collections.Generic;
using System.Reflection;
using FamilyTimeMultiplayer.Families;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch]
internal static class FamilyWorkerFilterPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(CommandOwnershipService), nameof(CommandOwnershipService.FindAvailableWorkers));
        yield return AccessTools.Method(typeof(CommandOwnershipService), nameof(CommandOwnershipService.FindReassignableWorkers));
    }

    private static void Prefix(ref Predicate<Handle> additionalFilter)
    {
        HostFamilyService? families = HostFamilyService.Current;
        if (families == null)
        {
            return;
        }
        additionalFilter = families.FilterActiveFamily(additionalFilter);
    }
}

[HarmonyPatch(typeof(CommandOwnershipService), nameof(CommandOwnershipService.FindUnavailableWorkers))]
internal static class FamilyUnavailableWorkerPatch
{
    private static void Postfix(ref Handle[] __result)
    {
        HostFamilyService? families = HostFamilyService.Current;
        if (families != null)
        {
            __result = families.ExcludeOtherFamiliesFromWorkers(__result);
        }
    }
}

[HarmonyPatch(typeof(CommandOwnershipService), nameof(CommandOwnershipService.AvailableWorkerCount), MethodType.Getter)]
internal static class FamilyAvailableWorkersPatch
{
    private static void Postfix(ref int __result)
    {
        HostFamilyService? families = HostFamilyService.Current;
        if (families != null)
        {
            __result = families.AvailableFor(families.ActiveOwner);
        }
    }
}

[HarmonyPatch(typeof(CommandOwnershipService), nameof(CommandOwnershipService.WorkerTemplateAgentCount), MethodType.Getter)]
internal static class FamilyWorkerTotalPatch
{
    private static bool Prepare() => typeof(CommandOwnershipService).GetMethod("ResolveWorkerPhysical", BindingFlags.Instance | BindingFlags.NonPublic) == null;

    private static void Postfix(ref int __result)
    {
        HostFamilyService? families = HostFamilyService.Current;
        if (families != null)
        {
            __result = families.ChildrenOf(families.ActiveOwner);
        }
    }
}

[HarmonyPatch]
internal static class FamilyHudDisplayPatch
{
    private static MethodBase TargetMethod() => Game.GameApiCompatibility.HudRefreshMethod;
    private static void Prefix(ref int ____totalPlayerCount, ref int ____hungryTotal)
    {
        FamilyHudCounts? counts = FamilyHud.Override;
        if (!counts.HasValue)
        {
            return;
        }
        ____totalPlayerCount = counts.Value.Children;
        ____hungryTotal = counts.Value.Hungry;
        FamilyHud.DisplayRefreshing = true;
    }

    private static void Finalizer() => FamilyHud.DisplayRefreshing = false;
}

[HarmonyPatch(typeof(GATStationManager), nameof(GATStationManager.GetSleepingInstanceCount))]
internal static class FamilyHudSleepingPatch
{
    private static void Postfix(ref int __result)
    {
        FamilyHudCounts? counts = FamilyHud.Override;
        if (FamilyHud.DisplayRefreshing && counts.HasValue)
        {
            __result = counts.Value.Sleeping;
        }
    }
}

[HarmonyPatch(typeof(GATStateDisplayUI), nameof(GATStateDisplayUI.GetHungryCount))]
internal static class FamilyHudHungryReadPatch
{
    private static void Postfix(ref int __result)
    {
        if (FamilyHud.Override is FamilyHudCounts counts) __result = counts.Hungry;
    }
}

[HarmonyPatch(typeof(GATStateDisplayUI), nameof(GATStateDisplayUI.GetAgentsNotSleepingCount))]
internal static class FamilyHudSleepReadPatch
{
    private static void Postfix(ref int __result)
    {
        if (FamilyHud.Override is FamilyHudCounts counts) __result = counts.NotSleeping;
    }
}

[HarmonyPatch(typeof(GATStateDisplayUI), "RefreshTotalPlayerCount")]
internal static class FamilyHudTotalRefreshPatch
{
    private static bool Prefix(GATStateDisplayUI __instance, ref int ____totalPlayerCount)
    {
        if (!(FamilyHud.Override is FamilyHudCounts counts)) return true;
        ____totalPlayerCount = counts.Children;
        Game.GameApiCompatibility.HudRefreshMethod.Invoke(__instance, null);
        return false;
    }
}

[HarmonyPatch]
internal static class FamilyHudHungerRequestPatch
{
    private static MethodBase TargetMethod() => Game.GameApiCompatibility.HudHungerUpdateMethod;
    private static bool Prefix() => !FamilyHud.Override.HasValue;
}

[HarmonyPatch(typeof(GATStateDisplayUI), nameof(GATStateDisplayUI.RunOvernight))]
internal static class FamilyHudOvernightRefreshPatch
{
    private static void Postfix()
    {
        if (FamilyHud.Override.HasValue) FamilyHud.Refresh();
    }
}
