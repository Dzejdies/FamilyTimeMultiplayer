using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FamilyTimeMultiplayer.Core.Saves;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch]
internal static class SaveCollectionPathPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        Type serializerType = typeof(WorldSerializer);
        MethodInfo? worldPath = AccessTools.Method(serializerType, "GetPath", new[] { typeof(string) });
        MethodInfo? sessionsDirectory = AccessTools.Method(serializerType, "GetSessionsFolder", Type.EmptyTypes);
        if (worldPath == null && sessionsDirectory == null)
            throw new MissingMethodException(serializerType.FullName, "GetPath or GetSessionsFolder");
        if (worldPath != null && worldPath.ReturnType == typeof(string)) yield return worldPath;
        if (sessionsDirectory != null && sessionsDirectory.ReturnType == typeof(string)) yield return sessionsDirectory;
    }

    private static bool Prefix(MethodBase __originalMethod, object[] __args, ref string __result)
    {
        SaveCollectionService saveCollections = SaveCollectionService.RequireCurrent();
        if (__originalMethod.Name == "GetSessionsFolder")
        {
            __result = saveCollections.GetSessionsDirectory();
            return false;
        }
        if (__originalMethod.Name != "GetPath" || __args.Length != 1 || __args[0] is not string mapName) return true;
        __result = saveCollections.GetWorldPath(mapName);
        return false;
    }
}

[HarmonyPatch]
internal static class NativeSaveLoadImportButtonPatch
{
    private const string SaveLoadMenuTypeName = "GAT.SaveLoadMenu";
    private const string ImportButtonName = "FamilyTimeMultiplayerImportCopy";

    private static bool Prepare() => typeof(WorldSerializer).Assembly.GetType(SaveLoadMenuTypeName) != null;

    private static IEnumerable<MethodBase> TargetMethods()
    {
        Type menuType = typeof(WorldSerializer).Assembly.GetType(SaveLoadMenuTypeName)
            ?? throw new TypeLoadException($"{SaveLoadMenuTypeName} was not found.");
        MethodInfo awake = AccessTools.Method(menuType, "Awake")
            ?? throw new MissingMethodException(menuType.FullName, "Awake");
        yield return awake;
    }

    private static void Postfix(object __instance)
    {
        Component menu = (Component)__instance;
        if (HasImportButton(menu)) return;
        Button saveButton = Traverse.Create(__instance).Field<Button>("saveButton").Value
            ?? throw new MissingFieldException(__instance.GetType().FullName, "saveButton");
        AddImportButton(saveButton);
    }

    private static bool HasImportButton(Component menu)
    {
        foreach (Button button in menu.GetComponentsInChildren<Button>(includeInactive: true))
            if (button.name == ImportButtonName) return true;
        return false;
    }

    private static void AddImportButton(Button saveButton)
    {
        GameObject buttonObject = UnityEngine.Object.Instantiate(saveButton.gameObject, saveButton.transform.parent, false);
        buttonObject.name = ImportButtonName;
        Button button = buttonObject.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(OpenImportFlow);
        TMP_Text? label = buttonObject.GetComponentInChildren<TMP_Text>(includeInactive: true);
        if (label != null)
        {
            label.text = "Import Copy";
            return;
        }
        Text legacyLabel = buttonObject.GetComponentInChildren<Text>(includeInactive: true)
            ?? throw new InvalidOperationException("The native save button has no text label.");
        legacyLabel.text = "Import Copy";
    }

    private static void OpenImportFlow() => MultiplayerController.Current?.OpenSaveImport();
}

[HarmonyPatch]
internal static class NativeInternalSaveListPatch
{
    private const string SessionLibraryTypeName = "GAT.SessionLibrary";

    private static bool Prepare() => typeof(WorldSerializer).Assembly.GetType(SessionLibraryTypeName) != null;

    private static IEnumerable<MethodBase> TargetMethods()
    {
        Type libraryType = typeof(WorldSerializer).Assembly.GetType(SessionLibraryTypeName)
            ?? throw new TypeLoadException($"{SessionLibraryTypeName} was not found.");
        MethodInfo loadEntries = AccessTools.Method(libraryType, "LoadEntries", new[] { typeof(string) })
            ?? throw new MissingMethodException(libraryType.FullName, "LoadEntries");
        yield return loadEntries;
    }

    private static void Postfix(object __result)
    {
        if (__result is not IList entries) return;
        for (int index = entries.Count - 1; index >= 0; index--)
            if (IsInternalEntry(entries[index])) entries.RemoveAt(index);
    }

    private static bool IsInternalEntry(object? entry)
    {
        if (entry == null) return false;
        object? value = AccessTools.Field(entry.GetType(), "name")?.GetValue(entry)
            ?? AccessTools.Property(entry.GetType(), "name")?.GetValue(entry);
        return value is string name && SaveCollectionPolicy.IsInternalSaveName(name);
    }
}

internal static class NativeSaveMenuRefresh
{
    public static void RefreshIfActive(string selectedName)
    {
        Type? menuType = typeof(WorldSerializer).Assembly.GetType("GAT.SaveLoadMenu");
        MethodInfo? refresh = menuType == null ? null : AccessTools.Method(menuType, "Refresh", new[] { typeof(string) });
        if (menuType == null || refresh == null) return;
        foreach (UnityEngine.Object instance in UnityEngine.Object.FindObjectsByType(menuType, FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (instance is Behaviour behaviour && behaviour.isActiveAndEnabled)
                refresh.Invoke(behaviour, new object?[] { selectedName });
        }
    }
}
