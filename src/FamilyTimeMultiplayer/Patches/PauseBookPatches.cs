using System;
using FamilyTimeMultiplayer.UI;
using System.Collections.Generic;
using System.Reflection;
using FamilyTimeMultiplayer.Core.Saves;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(PauseMenuBook), "Awake")]
internal static class PauseBookInstallMultiplayerPagePatch
{
    private static void Postfix(PauseMenuBook __instance)
    {
        MultiplayerController? controller = MultiplayerController.Current;
        if (controller != null)
        {
            PauseBookMultiplayerPage.Install(__instance, controller);
        }
    }
}

[HarmonyPatch]
internal static class NativeTitleMenuEntryPatch
{
    private static bool Prepare() => typeof(PauseMenuBook).Assembly.GetType("family.TitleMenu") != null;

    private static IEnumerable<MethodBase> TargetMethods()
    {
        Type? titleMenu = typeof(PauseMenuBook).Assembly.GetType("family.TitleMenu");
        MethodInfo? initialize = titleMenu == null ? null : HarmonyLib.AccessTools.Method(titleMenu, "Initialize");
        if (initialize != null) yield return initialize;
    }

    private static void Postfix(object __instance, object __0)
    {
        MultiplayerController? controller = MultiplayerController.Current;
        if (controller == null) return;
        UnityEngine.Component titleMenu = (UnityEngine.Component)__instance;
        TitleMenuEntryButtons? existing = titleMenu.GetComponent<TitleMenuEntryButtons>();
        if (existing != null) return;
        TitleMenuEntryButtons buttons = titleMenu.gameObject.AddComponent<TitleMenuEntryButtons>();
        buttons.Initialize(
            () =>
            {
                controller.SelectSaveCollection(SaveCollection.Solo);
                GameApiCompatibility.BeginNativeTitle(__0);
            },
            () =>
            {
                controller.OpenMultiplayerFromTitle();
                GameApiCompatibility.BeginNativeTitle(__0);
            });
    }
}

[HarmonyPatch]
internal static class NativeTitleMenuInputPatch
{
    private static bool Prepare() => typeof(PauseMenuBook).Assembly.GetType("family.TitleMenu") != null;

    private static IEnumerable<MethodBase> TargetMethods()
    {
        Type? titleMenu = typeof(PauseMenuBook).Assembly.GetType("family.TitleMenu");
        MethodInfo? update = titleMenu == null ? null : HarmonyLib.AccessTools.Method(titleMenu, "Update");
        if (update != null) yield return update;
    }

    private static bool Prefix(object __instance) =>
        ((UnityEngine.Component)__instance).GetComponent<TitleMenuEntryButtons>() == null;
}
