using System;
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
    private static WorldPauseScopeLease? _startupPauseLease;

    public static void ReleasePendingPauseScope()
    {
        WorldPauseScopeLease? lease = _startupPauseLease;
        _startupPauseLease = null;
        lease?.Dispose();
    }

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
        WorldPauseScopeLease? waitPause = null;
        IEnumerator? generation = null;
        try
        {
            WorldSerializer.WorldPauseScope? nativePause = World.worldSerializer != null
                ? World.worldSerializer.BeginWorldPause("Choose how to play")
                : null;
            if (nativePause != null)
            {
                waitPause = new WorldPauseScopeLease(nativePause);
                _startupPauseLease = waitPause;
            }
            while (StartupGate.IsAwaitingChoice)
            {
                yield return null;
            }
            WorldPauseScopeLease? completedWaitPause = waitPause;
            waitPause = null;
            if (ReferenceEquals(_startupPauseLease, completedWaitPause)) _startupPauseLease = null;
            completedWaitPause?.Dispose();
            _passThrough = true;
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
        finally
        {
            try
            {
                (generation as IDisposable)?.Dispose();
            }
            finally
            {
                if (ReferenceEquals(_startupPauseLease, waitPause)) _startupPauseLease = null;
                waitPause?.Dispose();
            }
        }
    }
}
