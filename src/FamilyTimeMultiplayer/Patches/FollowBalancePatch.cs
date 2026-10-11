using FamilyTimeMultiplayer.Commands;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(FollowController), nameof(FollowController.StartFollowing), new System.Type[0])]
internal static class FollowBalancePatch
{
    private static void Prefix(FollowController __instance)
    {
        if (!__instance.IsFollowing && Families.HostFamilyService.Current == null)
        {
            HostCommandHub.Current?.BalanceBeforeFollow(__instance);
        }
    }
}
