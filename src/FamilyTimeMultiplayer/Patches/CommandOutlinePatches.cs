using System.Collections.Generic;
using FamilyTimeMultiplayer.Commands;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(CommandVisuals), nameof(CommandVisuals.RefreshSelectionOutline))]
internal static class CommandSelectionOutlinePatch
{
    private static bool Prefix(CommandVisuals __instance, ref ICollection<RigidTransform> selectedRigids)
    {
        if (RemoteCommandOutlines.TryGet(__instance, out RemoteCommandOutlines remote))
        {
            remote.Select(selectedRigids);
            return false;
        }
        if (ClientCommandOutlines.Owns(__instance)) selectedRigids = ClientCommandOutlines.Selected;
        return true;
    }
}

[HarmonyPatch(typeof(CommandVisuals), nameof(CommandVisuals.RefreshPrimaryOutline))]
internal static class CommandPrimaryOutlinePatch
{
    private static bool Prefix(CommandVisuals __instance, ref List<CommandVisuals.PrimaryOutlineEntry> entries)
    {
        if (RemoteCommandOutlines.TryGet(__instance, out RemoteCommandOutlines remote))
        {
            remote.SetPrimary(entries);
            return false;
        }
        if (ClientCommandOutlines.Owns(__instance)) entries = ClientCommandOutlines.PrimaryEntries;
        return true;
    }
}

[HarmonyPatch(typeof(CommandVisuals), nameof(CommandVisuals.UpdateHoverOutline))]
internal static class CommandHoverOutlinePatch
{
    private static bool Prefix(CommandVisuals __instance, ref ICollection<RigidTransform> selectedRigids)
    {
        if (RemoteCommandOutlines.TryGet(__instance, out _)) return false;
        if (ClientCommandOutlines.Owns(__instance)) selectedRigids = ClientCommandOutlines.Selected;
        return true;
    }
}

[HarmonyPatch(typeof(CommandVisuals), nameof(CommandVisuals.ClearOutline))]
internal static class CommandClearOutlinePatch
{
    private static bool Prefix(CommandVisuals __instance)
    {
        if (!RemoteCommandOutlines.TryGet(__instance, out RemoteCommandOutlines remote))
        {
            if (ClientCommandOutlines.Owns(__instance)) ClientCommandOutlines.Invalidate();
            return true;
        }
        remote.Clear();
        return false;
    }
}

[HarmonyPatch(typeof(CommandService), "Update")]
internal static class ClientCommandOutlineRefreshPatch
{
    private static void Postfix(CommandService __instance) => ClientCommandOutlines.Refresh(__instance);
}
