using System.Collections;
using System.Reflection;
using FamilyTimeMultiplayer.UI;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(ProceduralMapManager), "GenerateStartupRoutine")]
internal static class WorldGenerationGatePatch
{
    private static readonly MethodInfo Original = AccessTools.Method(typeof(ProceduralMapManager), "GenerateStartupRoutine");

    private static bool _passThrough;

    private static bool Prefix(ProceduralMapManager __instance, ref IEnumerator __result)
    {
        if (_passThrough || !StartupGate.IsAwaitingChoice)
        {
            return true;
        }
        __result = WaitForChoiceThenGenerate(__instance);
        return false;
    }

    private static IEnumerator WaitForChoiceThenGenerate(ProceduralMapManager manager)
    {
        WorldSerializer.WorldPauseScope? waitPause = World.worldSerializer != null
            ? World.worldSerializer.BeginWorldPause("Choose how to play")
            : null;
        while (StartupGate.IsAwaitingChoice)
        {
            yield return null;
        }
        waitPause?.Dispose();
        _passThrough = true;
        IEnumerator generation;
        try
        {
            generation = (IEnumerator)Original.Invoke(manager, null);
        }
        finally
        {
            _passThrough = false;
        }
        while (generation.MoveNext())
        {
            yield return generation.Current;
        }
    }
}
