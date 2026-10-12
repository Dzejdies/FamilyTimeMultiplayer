using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using FamilyTimeMultiplayer.Core.Launch;
using FamilyTimeMultiplayer.Game;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer;

[BepInPlugin(Guid, Name, Version)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "dzejdies.familytime.multiplayer";
    public const string Name = "Family Time Multiplayer";
    public const string Version = "0.2.0";

    private void Awake()
    {
        SaveCollectionService saveCollections = SaveCollectionService.Initialize();
        Diagnostics.PersistentLog persistent = Diagnostics.PersistentLog.Start();
        BepInEx.Logging.Logger.Listeners.Add(persistent);
        Logger.LogInfo($"[Log] this run is also written to {persistent.Path}");
        MultiplayerSettings settings = MultiplayerSettings.Bind(Config);
        LaunchOptions launch = LaunchOptions.Parse(Environment.GetCommandLineArgs());
        Harmony harmony = new Harmony(Guid);
        harmony.PatchAll(typeof(Plugin).Assembly);
        Logger.LogInfo($"{Name} {Version}: patched {harmony.GetPatchedMethods().Count()} methods, launch action {launch.Action}");
        LogSavePathPatches(harmony);

        var host = new GameObject("FamilyTimeMultiplayer") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(host);
        host.AddComponent<MultiplayerController>().Initialize(Logger, settings, launch, saveCollections);
        host.AddComponent<Families.FamilyWorldPersistence>().Initialize(Logger, settings);
    }

    private void LogSavePathPatches(Harmony harmony)
    {
        MethodBase[] patchedMethods = harmony.GetPatchedMethods().ToArray();
        string serializerMethods = string.Join(", ", patchedMethods
            .Where(method => method.DeclaringType == typeof(GAT.WorldSerializer) &&
                (method.Name == "GetPath" || method.Name == "GetSessionsFolder"))
            .Select(method => method.Name)
            .Distinct());
        Logger.LogInfo($"[Saves] redirected serializer methods: {serializerMethods}");
        if (!GameApiCompatibility.HasNativeTitleFlow) return;
        bool importButton = patchedMethods.Any(method => method.DeclaringType?.FullName == "GAT.SaveLoadMenu" && method.Name == "Awake");
        bool internalListFilter = patchedMethods.Any(method => method.DeclaringType?.FullName == "GAT.SessionLibrary" && method.Name == "LoadEntries");
        Logger.LogInfo($"[Saves] native import button patched: {importButton}, internal list filter patched: {internalListFilter}");
    }
}
