using System.Collections.Generic;
using FamilyTimeMultiplayer.Families;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Patches;

internal static class MamaProximityRotation
{
    private static int _next;

    public static Dictionary<string, (int Samples, int Looks)> Stats { get; } = new Dictionary<string, (int, int)>();

    public static void CountLook()
    {
        if (Sampled != null)
        {
            (int samples, int looks) = Stats.TryGetValue(Sampled.name, out var current) ? current : (0, 0);
            Stats[Sampled.name] = (samples, looks + 1);
        }
    }

    public static WolfMama? Sampled { get; private set; }

    public static WolfMama? PickNext()
    {
        WolfMama? original = WolfMama.ActiveInstance;
        IReadOnlyList<WolfMama> clones = MamaCloning.LiveClones;
        int count = clones.Count + (original != null ? 1 : 0);
        if (count == 0)
        {
            Sampled = null;
            return null;
        }
        int slot = _next++ % count;
        Sampled = original != null ? slot == 0 ? original : clones[slot - 1] : clones[slot];
        (int samples, int looks) = Stats.TryGetValue(Sampled.name, out var current) ? current : (0, 0);
        Stats[Sampled.name] = (samples + 1, looks);
        return Sampled;
    }
}

[HarmonyPatch(typeof(GATCharacterProximityBehavior), "TryGetWolfMamaSource")]
internal static class MamaProximitySourcePatch
{
    private static bool Prefix(ref bool __result, ref Vector3 sourcePosition, ref int sourcePhysicalIndex)
    {
        if (MamaCloning.LiveClones.Count == 0)
        {
            return true;
        }
        sourcePosition = Vector3.zero;
        sourcePhysicalIndex = -1;
        __result = false;
        WolfMama? mama = MamaProximityRotation.PickNext();
        if (mama == null || World.gat == null || !mama.TryGetHandle(out Handle handle))
        {
            return false;
        }
        ushort physical = World.gat.ResolveHandle(handle);
        if (physical == ushort.MaxValue)
        {
            return false;
        }
        sourcePosition = mama.transform.position;
        sourcePhysicalIndex = physical;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(GATCharacterProximityBehavior), "OnDataReadback")]
internal static class MamaProximityLookPatch
{
    private static void Prefix(bool ____readbackIsWolfMama, out WolfMama? __state)
    {
        __state = null;
        WolfMama? sampled = MamaProximityRotation.Sampled;
        WolfMama? active = WolfMama.ActiveInstance;
        if (____readbackIsWolfMama)
        {
            MamaProximityRotation.CountLook();
        }
        if (____readbackIsWolfMama && sampled != null && active != null && sampled != active)
        {
            __state = active;
            MamaCloning.SetActiveInstance(sampled);
        }
    }

    private static void Finalizer(WolfMama? __state)
    {
        if (__state != null)
        {
            MamaCloning.SetActiveInstance(__state);
        }
    }
}
