using FamilyTimeMultiplayer.UI;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(PauseMenuBook), "Awake")]
internal static class PauseBookInstallMultiplayerPagePatch
{
    private static void Postfix(PauseMenuBook __instance)
    {
        MultiplayerController? controller = MultiplayerController.Current;
        if (controller != null)
        {
            PauseBookMultiplayerPage.Install(__instance, controller);
        }
    }
}
