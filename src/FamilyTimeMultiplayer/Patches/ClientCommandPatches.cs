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

[HarmonyPatch(typeof(CommandOwnershipService), nameof(CommandOwnershipService.WorkerTemplateAgentCount), MethodType.Getter)]
internal static class ClientGlobalPoolTotalPatch
{
    private static void Postfix(ref int __result)
    {
        if (ClientCommandBridge.IsActive && ClientCommandBridge.HostTotalWorkers.HasValue)
        {
            __result = ClientCommandBridge.HostTotalWorkers.Value;
        }
    }
}
