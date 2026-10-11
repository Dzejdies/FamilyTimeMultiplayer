using System;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(GATChunkManager), "LateUpdate")]
internal static class ClientSkipsChunkStreamingPatch
{
    private static bool Prefix() => !ReplicationGate.BlocksLocalSimulation;
}

[HarmonyPatch(typeof(DayManager), nameof(DayManager.BeginNewDay))]
internal static class ClientSkipsNewDayPatch
{
    private static bool Prefix() => !ReplicationGate.BlocksLocalSimulation;
}

[HarmonyPatch(typeof(DayManager), nameof(DayManager.PassTime))]
internal static class ClientSkipsPassTimePatch
{
    private static bool Prefix() => !ReplicationGate.BlocksLocalSimulation;
}

[HarmonyPatch(typeof(OvernightRunner), nameof(OvernightRunner.Run))]
internal static class ClientSkipsOvernightPatch
{
    private static bool Prefix() => !ReplicationGate.BlocksLocalSimulation;
}

[HarmonyPatch(typeof(Singleplayer), nameof(Singleplayer.GrowChildren))]
internal static class ClientSkipsChildGrowthPatch
{
    private static bool Prefix(Action onComplete)
    {
        if (!ReplicationGate.BlocksLocalSimulation)
        {
            return true;
        }
        onComplete?.Invoke();
        return false;
    }
}

[HarmonyPatch(typeof(PlayerController), "OnQuickSavePerformed")]
internal static class ClientSkipsQuickSavePatch
{
    private static bool Prefix() => ReplicationGate.Role != SessionRole.Client;
}

[HarmonyPatch(typeof(PlayerController), "OnQuickLoadPerformed")]
internal static class ClientSkipsQuickLoadPatch
{
    private static bool Prefix() => ReplicationGate.Role != SessionRole.Client;
}

[HarmonyPatch(typeof(WorldSerializer), nameof(WorldSerializer.Load))]
internal static class HostWorldReloadPatch
{
    private static void Prefix(string mapName, ref Action<bool> onComplete)
    {
        FamilyTimeMultiplayer.Families.FamilyWorldPersistence.DetachBeforeWorldLoad(mapName);
        if (ReplicationGate.Role != SessionRole.Host)
        {
            return;
        }
        Action<bool> original = onComplete;
        onComplete = success =>
        {
            original?.Invoke(success);
            if (success && ReplicationGate.Role == SessionRole.Host)
            {
                ReplicationGate.HostObserver?.OnHostWorldReloaded();
            }
        };
    }
}
