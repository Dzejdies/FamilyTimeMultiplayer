using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.PossessedHandle), MethodType.Getter)]
internal static class CommanderPossessedHandlePatch
{
    private static void Postfix(ref Handle __result)
    {
        if (CommanderContext.Active != null)
        {
            __result = CommanderContext.Active.AvatarHandle;
        }
    }
}

[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.CachedInstancePosition), MethodType.Getter)]
internal static class CommanderPositionPatch
{
    private static void Postfix(ref Vector3 __result)
    {
        if (CommanderContext.Active != null)
        {
            __result = CommanderContext.Active.AvatarPosition;
        }
    }
}

[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.hand), MethodType.Getter)]
internal static class CommanderHandPatch
{
    private static bool Prefix(ref HandService __result)
    {
        if (CommanderContext.Active == null)
        {
            return true;
        }
        __result = null!;
        return false;
    }
}

[HarmonyPatch]
internal static class CommanderRoutesAudioPatch
{
    private static System.Reflection.MethodBase TargetMethod() =>
        AccessTools.Method(AccessTools.TypeByName("GAT.CommandAudio") ?? throw new System.TypeLoadException("GAT.CommandAudio not found"), "Play")
        ?? throw new System.MissingMethodException("GAT.CommandAudio", "Play");

    private static bool Prefix(AudioGroup audioGroup)
    {
        IRemoteCommanderIdentity? commander = CommanderContext.Active;
        if (commander == null)
        {
            return true;
        }
        HostCommandHub.Current?.QueueAudio(commander.PlayerId, audioGroup);
        return false;
    }
}

[HarmonyPatch]
internal static class CommanderFollowCursorPatch
{
    private const float CommanderCursorRangeFallback = 40f;

    private static System.Reflection.MethodBase TargetMethod() => GameApiCompatibility.FollowCursorTargetMethod;

    private static bool Prefix(FollowController __instance)
    {
        IRemoteCommanderIdentity? commander = CommanderContext.Active;
        if (commander == null)
        {
            return true;
        }
        if (World.navManager == null)
        {
            return false;
        }
        Traverse fields = Traverse.Create(__instance);
        GameApiCompatibility.ActivateFollowCursor(__instance);
        float range = fields.Field<float>("_followCursorMaxDistance").Value;
        if (!World.Raycast(commander.Eye.position, commander.Eye.forward, range > 0f ? range : CommanderCursorRangeFallback, out World.HitInfo hit, CPUCollisionMask.DEFAULT))
        {
            return false;
        }
        Vector3Int cell = Vector3Int.FloorToInt(hit.point);
        if (!World.navManager.IsWalkableSurface(cell))
        {
            return false;
        }
        bool changed = !fields.Field<bool>("_hasFollowCursorTarget").Value || fields.Field<Vector3Int>("_followCursorTargetCell").Value != cell;
        fields.Field<Vector3Int>("_followCursorTargetCell").Value = cell;
        fields.Field<Vector3>("_followCursorTargetPosition").Value = hit.point;
        fields.Field<bool>("_hasFollowCursorTarget").Value = true;
        int followers = GameApiCompatibility.FollowAgentCount(__instance);
        PickupWorkerScheduler pickup = fields.Field<PickupWorkerScheduler>("_pickup").Value;
        if (pickup.SelectedCount <= 0 && changed && followers > 0)
        {
            __instance.RequestFollowFlowField(followers);
        }
        return false;
    }
}
