using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(CommandOwnershipService), nameof(CommandOwnershipService.AvailableWorkerCount), MethodType.Getter)]
internal static class ClientGlobalPoolPatch
{
    private static void Postfix(ref int __result)
    {
        if (ClientCommandBridge.IsActive && ClientCommandBridge.HostAvailableWorkers.HasValue)
        {
            __result = ClientCommandBridge.HostAvailableWorkers.Value;
        }
    }
}
