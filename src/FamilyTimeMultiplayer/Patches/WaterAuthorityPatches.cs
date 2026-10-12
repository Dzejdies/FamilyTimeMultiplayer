using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(WaterManager), "DispatchSimulation")]
internal static class ClientWaterFlowPatch
{
    private static bool Prefix() => ReplicationGate.Role != SessionRole.Client;
}

[HarmonyPatch(typeof(WaterManager), "DispatchTerrainCarve")]
internal static class ClientWaterCarvePatch
{
    private static bool Prefix() => ReplicationGate.Role != SessionRole.Client;
}

[HarmonyPatch(typeof(WaterManager), "ModifyWaterAtCell")]
internal static class ClientWaterMutationPatch
{
    private static bool Prefix(ref bool __result)
    {
        if (!ReplicationGate.BlocksLocalSimulation) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(GATHungerBehavior), nameof(GATHungerBehavior.PerformDrink))]
internal static class ClientWaterDrinkPatch
{
    private static bool Prefix() => ReplicationGate.Role != SessionRole.Client;
}

[HarmonyPatch(typeof(GATHungerBehavior), "DispatchNextDrinkBatch")]
internal static class ClientWaterDrinkDispatchPatch
{
    private static bool Prefix() => ReplicationGate.Role != SessionRole.Client;
}

[HarmonyPatch(typeof(TerrainLifecycle), nameof(TerrainLifecycle.RunOvernight))]
internal static class ClientWaterEvaporationPatch
{
    private static bool Prefix() => ReplicationGate.Role != SessionRole.Client;
}
