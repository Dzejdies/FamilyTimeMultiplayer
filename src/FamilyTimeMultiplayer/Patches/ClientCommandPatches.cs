using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(CommandService), "OnFollowStarted")]
internal static class ClientFollowStartedPatch
{
    private static bool Prefix(bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!ClientCommandBridge.IsActive)
        {
            return true;
        }
        if (!PlayerController.IsTypingInInputField)
        {
            ClientCommandBridge.Send(CommandIntentMessage.Follow(pressed: true));
        }
        return false;
    }
}

[HarmonyPatch(typeof(CommandService), "OnFollowCanceled")]
internal static class ClientFollowCanceledPatch
{
    private static bool Prefix(bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!ClientCommandBridge.IsActive)
        {
            return true;
        }
        ClientCommandBridge.Send(CommandIntentMessage.Follow(pressed: false));
        return false;
    }
}

[HarmonyPatch(typeof(CommandService), "OnInteract")]
internal static class ClientInteractPatch
{
    private static bool Prefix(CommandService __instance, bool started, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!ClientCommandBridge.IsActive)
        {
            return true;
        }
        if (started)
        {
            CommandIntentMessage? intent = ClientCommandBridge.ResolveInteractTarget(__instance);
            if (intent != null)
            {
                ClientCommandBridge.Send(intent);
            }
        }
        return false;
    }
}

[HarmonyPatch]
internal static class ClientPickupSweepPatch
{
    [HarmonyTargetMethod]
    private static System.Reflection.MethodBase TargetMethod() => HarmonyLib.AccessTools.Method(typeof(CommandService), "OnLongPressInteract")!;

    [HarmonyPrefix]
    private static bool Prefix(CommandService __instance, bool started, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!ClientCommandBridge.IsActive)
        {
            return true;
        }
        if (started)
        {
            CommandIntentMessage? intent = ClientCommandBridge.ResolvePickupSweepTarget(__instance);
            if (intent != null)
            {
                ClientCommandBridge.Send(intent);
            }
            else
            {
                ClientCommandBridge.Send(CommandIntentMessage.PickupSweep(-1, started: false));
                ClientCommandBridge.ShowNotice("Multi-select currently supports loose items; attack and bucket sweeps are not synced.");
            }
        }
        else
        {
            ClientCommandBridge.Send(CommandIntentMessage.PickupSweep(-1, started: false));
        }
        return false;
    }
}

[HarmonyPatch]
internal static class ClientUnsupportedLongPressSecondaryPatch
{
    [HarmonyTargetMethod]
    private static System.Reflection.MethodBase TargetMethod() => GameApiCompatibility.CommandLongPressSecondaryTargetMethod;

    [HarmonyPrefix]
    private static bool Prefix(bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!ClientCommandBridge.IsActive) return true;
        ClientCommandBridge.ShowNotice("Hold-to-select locks is not synced yet.");
        return false;
    }
}

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

[HarmonyPatch(typeof(CommandService), "OnCommandLockClick")]
internal static class ClientCommandLockPatch
{
    private static bool Prefix(bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!ClientCommandBridge.IsActive)
        {
            return true;
        }
        CommandIntentMessage? intent = ClientCommandBridge.ResolveCommandLockTarget();
        if (intent != null)
        {
            ClientCommandBridge.Send(intent);
            return false;
        }
        HandService? hand = PlayerController.hand;
        return hand?.HandCarriedRigid?.HolsterCarryInfo != null;
    }
}
