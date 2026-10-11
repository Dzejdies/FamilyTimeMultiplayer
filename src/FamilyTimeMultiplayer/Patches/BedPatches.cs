using System;
using System.Reflection;
using FamilyTimeMultiplayer.Families;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(BedManager), "BeginSleep")]
internal static class BedBeginSleepPatch
{
    private static bool Prefix(BedManager __instance)
    {
        if (ClientNight.IsActive)
        {
            ClientNight.GoToBed();
            return false;
        }
        HostNightCoordinator? night = HostNightCoordinator.Current;
        return night == null || night.RequestHostSleep(__instance);
    }
}

[HarmonyPatch(typeof(BedManager), nameof(BedManager.OnPrimary))]
internal static class BedGetUpPatch
{
    private static bool Prefix()
    {
        bool inBed = ClientNight.IsActive ? ClientNight.IsReady : HostNightCoordinator.Current?.HostIsReady == true;
        if (!inBed || World.promptUI == null)
        {
            return true;
        }
        World.promptUI.Prompt(false, "Get out of bed?", consent =>
        {
            if (!consent)
            {
                return;
            }
            if (ClientNight.IsActive)
            {
                ClientNight.GetUp();
            }
            else
            {
                HostNightCoordinator.Current?.HostGetsUp();
            }
        });
        return false;
    }
}

[HarmonyPatch(typeof(BedManager), "PlaySleepMovies")]
internal static class BedNightOutcomePatch
{
    private static void Prefix()
    {
        if (HostNightCoordinator.Current != null)
        {
            HostNightCoordinator.Current.OnHostPlaysSleepMovies();
            return;
        }
        HostFamilyService? families = HostFamilyService.Current;
        families?.PlanNight(new[] { (families.HostOwner, Game.LocalPlayer.Position) });
    }
}

[HarmonyPatch(typeof(BedManager), "CompleteSleepFadeToBlack")]
internal static class BedSeparateGrowthPatch
{
    private static readonly MethodInfo WaitBeforeFadeFromBlack =
        AccessTools.Method(typeof(BedManager), "WaitBeforeFadeFromBlack")
        ?? throw new MissingMethodException(nameof(BedManager), "WaitBeforeFadeFromBlack");

    private static bool Prefix(BedManager __instance)
    {
        HostFamilyService? families = HostFamilyService.Current;
        if (families == null)
        {
            return true;
        }
        families.RunPlannedGrowth(() => WaitBeforeFadeFromBlack.Invoke(__instance, null));
        return false;
    }
}
