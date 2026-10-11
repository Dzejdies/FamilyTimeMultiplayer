using FamilyTimeMultiplayer.Commands;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(CommandService), "LateUpdate")]
internal static class CommandMarkerPatch
{
    private static void Postfix(CommandService __instance) => ClientCommandMarkers.Draw(__instance);
}
