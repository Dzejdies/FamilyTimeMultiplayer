using System;
using System.IO;
using FamilyTimeMultiplayer.Core.Saves;
using UnityEngine;

namespace FamilyTimeMultiplayer.Game;

internal sealed class SaveCollectionService
{
    private const string SessionsFolderName = "Sessions";
    private const string MultiplayerFolderName = "Multiplayer";
    private const string Build37SaveExtension = ".world";
    private const string Build91SaveExtension = ".family";
    private static SaveCollectionService? _current;
    private readonly SaveCollectionSelection _selection = new SaveCollectionSelection();

    private SaveCollectionService()
    {
    }

    public static SaveCollectionService Initialize() => _current ??= new SaveCollectionService();

    public static SaveCollectionService RequireCurrent() =>
        _current ?? throw new InvalidOperationException("The save collection service has not been initialized.");

    public SaveCollection SelectedCollection => _selection.Current;

    public void Select(SaveCollection collection) => _selection.Select(collection);

    public string GetSessionsDirectory()
    {
        string directory = GetDirectory(_selection.Current);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public string GetDirectory(SaveCollection collection)
    {
        return SaveCollectionPaths.GetDirectory(GetNativeSaveDirectory(), collection, MultiplayerFolderName);
    }

    public string GetWorldPath(string mapName)
    {
        ValidateMapName(mapName);
        string root = SaveCollectionPolicy.IsInternalSaveName(mapName)
            ? GetNativeSaveDirectory()
            : GetDirectory(_selection.Current);
        Directory.CreateDirectory(root);
        return Path.Combine(root, mapName + GetNativeSaveExtension());
    }

    public string GetNativeSaveExtension() => GameApiCompatibility.HasNativeTitleFlow
        ? Build91SaveExtension
        : Build37SaveExtension;

    private static string GetNativeSaveDirectory() => GameApiCompatibility.HasNativeTitleFlow
        ? Path.Combine(Application.dataPath, SessionsFolderName)
        : Application.persistentDataPath;

    private static void ValidateMapName(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName) || mapName == "." || mapName == ".." ||
            mapName.IndexOfAny(new[] { '/', '\\' }) >= 0 || Path.GetFileName(mapName) != mapName)
            throw new ArgumentException("The save name must be a single path segment.", nameof(mapName));
    }
}
