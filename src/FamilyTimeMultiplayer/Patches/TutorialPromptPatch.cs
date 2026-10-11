using System;
using System.Collections;
using System.Reflection;
using FamilyTimeMultiplayer.UI;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch]
internal static class TutorialPromptPatch
{
    private static bool Prepare() => typeof(TutorialManager).GetMethod("InitializeTutorial", BindingFlags.Instance | BindingFlags.NonPublic) != null;
    private static MethodBase TargetMethod() => typeof(TutorialManager).GetMethod("InitializeTutorial", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(nameof(TutorialManager), "InitializeTutorial");

    private static MethodInfo ResumeTutorial =>
        AccessTools.Method(typeof(TutorialManager), "ResumeTutorial") ?? throw new MissingMethodException(nameof(TutorialManager), "ResumeTutorial");

    private static MethodInfo HandleTutorialPrompt =>
        AccessTools.Method(typeof(TutorialManager), "HandleTutorialPrompt") ?? throw new MissingMethodException(nameof(TutorialManager), "HandleTutorialPrompt");

    private static bool Prefix(TutorialManager __instance, ref IEnumerator __result)
    {
        __result = InitializeAfterStartupMenu(__instance);
        return false;
    }

    private static IEnumerator InitializeAfterStartupMenu(TutorialManager tutorial)
    {
        yield return null;
        while (StartupGate.IsHolding)
        {
            yield return null;
        }
        if (tutorial == null)
        {
            yield break;
        }
        Traverse state = Traverse.Create(tutorial);
        if (state.Field<bool>("promptHandled").Value)
        {
            if (state.Field<bool>("tutorialAccepted").Value && state.Field<bool>("tutorialStarted").Value)
            {
                ResumeTutorial.Invoke(tutorial, null);
            }
        }
        else if (World.promptUI == null)
        {
            Debug.LogWarning("[TutorialManager] Tutorial prompt could not be shown because PromptUI is missing.");
        }
        else
        {
            var onAnswer = (Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), tutorial, HandleTutorialPrompt);
            World.promptUI.Prompt(alert: false, "Play Tutorial?", onAnswer);
        }
    }
}
