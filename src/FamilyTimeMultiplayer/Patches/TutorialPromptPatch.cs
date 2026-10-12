using System;
using System.Collections;
using System.Reflection;
using FamilyTimeMultiplayer.UI;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch]
internal static class TutorialPromptPatch
{
    private static bool Prepare() => typeof(TutorialManager).GetMethod("InitializeTutorial", BindingFlags.Instance | BindingFlags.NonPublic) != null;
    private static MethodBase TargetMethod() => typeof(TutorialManager).GetMethod("InitializeTutorial", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(nameof(TutorialManager), "InitializeTutorial");

    private static void Postfix(ref IEnumerator __result) => __result = WaitForStartupChoice(__result);

    private static IEnumerator WaitForStartupChoice(IEnumerator nativeTutorial)
    {
        try
        {
            while (StartupGate.IsHolding) yield return null;
            while (nativeTutorial.MoveNext()) yield return nativeTutorial.Current;
        }
        finally
        {
            (nativeTutorial as IDisposable)?.Dispose();
        }
    }
}
