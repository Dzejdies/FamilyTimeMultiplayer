using System;
using System.Reflection;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.WorldEdits;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(WaterManager), nameof(WaterManager.SpawnWaterAtCell))]
internal static class WaterSpawnReplicationPatch
{
    private static bool Prefix(Vector2Int worldCell, float waterLevel, ref bool __result)
    {
        if (!WorldEditRelay.ClientForwards)
        {
            return true;
        }
        __result = false;
        return false;
    }


}

[HarmonyPatch]
internal static class AgentNameReplicationPatch
{
    private static MethodBase TargetMethod() =>
        typeof(GATNames).GetMethod("SetName", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(Handle), typeof(string), typeof(bool) }, null)
        ?? typeof(GATNames).GetMethod("SetName", new[] { typeof(Handle), typeof(string) })
        ?? throw new MissingMethodException(nameof(GATNames), "SetName");
    private static void Postfix(GATNames __instance, bool __result, Handle handle)
    {
        if (!__result)
        {
            return;
        }
        string name = __instance.TryGetName(handle, out string current) ? current : string.Empty;
        if (name.Length > WorldEdit.MaxNameLength)
        {
            return;
        }
        if (WorldEditRelay.HostRecords)
        {
            WorldEditRelay.Record(WorldEdit.AgentName(handle.stableIndex, name));
        }
        else if (WorldEditRelay.ClientForwards && Commands.ClientCommandBridge.TryGetHostAgent(handle.stableIndex, out ushort host))
        {
            WorldEditRelay.Forward(WorldEdit.AgentName(host, name));
        }
    }
}

[HarmonyPatch(typeof(WeatherManager), nameof(WeatherManager.StartLightning))]
internal static class ClientLightningLoopPatch
{
    private static bool Prefix(WeatherManager __instance) => WeatherStateBridge.StartClientLightning(__instance);
}

[HarmonyPatch(typeof(WeatherManager), nameof(WeatherManager.StartFlashFloods))]
internal static class ClientFloodLoopPatch
{
    private static bool Prefix(WeatherManager __instance) => WeatherStateBridge.StartClientFloods(__instance);
}

[HarmonyPatch(typeof(WeatherManager), "AnimateLightning")]
internal static class LightningReplicationPatch
{
    private static void Prefix(Vector3 position) => WorldEditRelay.Record(WorldEdit.Lightning(GatInterop.ToNet(position)));
}

[HarmonyPatch(typeof(CommandService), nameof(CommandService.StartleChildren))]
internal static class ClientSkipsThunderCommandsPatch
{
    private static bool Prefix() => ReplicationGate.Role != SessionRole.Client;
}
