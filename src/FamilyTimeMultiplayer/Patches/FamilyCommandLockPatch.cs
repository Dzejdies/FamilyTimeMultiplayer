using FamilyTimeMultiplayer.Families;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(CommandOwnershipService), nameof(CommandOwnershipService.IsCommandLockCandidate))]
internal static class FamilyCommandLockPatch
{
    private static void Postfix(Handle __0, ref bool __result)
    {
        if (ReplicationGate.Role == SessionRole.Client && !ReplicationGate.BlocksLocalSimulation) return;
        if (__result) __result = LocalFamilyView.IncludesChild(__0);
    }
}
