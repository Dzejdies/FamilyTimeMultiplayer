using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using FamilyTimeMultiplayer.Core.Saves;
using FamilyTimeMultiplayer.UI;
using HarmonyLib;
using BepInEx.Logging;
using GAT;

namespace FamilyTimeMultiplayer.Game;

internal sealed class NativeSaveCatalog
{
    private readonly ManualLogSource _log;
    private readonly string _soloDirectory;
    private readonly string _multiplayerDirectory;
    private readonly string _extension;
    private readonly bool _usesSessionLibrary;

    public NativeSaveCatalog(SaveCollectionService saveCollections, ManualLogSource log)
    {
        _log = log;
        _soloDirectory = saveCollections.GetDirectory(SaveCollection.Solo);
        _multiplayerDirectory = saveCollections.GetDirectory(SaveCollection.Multiplayer);
        _extension = saveCollections.GetNativeSaveExtension();
        _usesSessionLibrary = GameApiCompatibility.HasNativeTitleFlow;
    }

    public IReadOnlyList<SaveImportEntry> LoadEntries()
    {
        var entries = new List<SaveImportEntry>();
        AddCollectionEntries(entries, SaveCollection.Solo, _soloDirectory);
        AddCollectionEntries(entries, SaveCollection.Multiplayer, _multiplayerDirectory);
        entries.Sort(CompareEntries);
        return entries;
    }

    private void AddCollectionEntries(List<SaveImportEntry> entries, SaveCollection collection, string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (NativeSaveRecord record in EnumerateRecords(directory))
        {
            if (!IsSelectable(record)) continue;
            if (TryValidateRecord(record)) entries.Add(new SaveImportEntry(collection, record.Name, record.Path));
        }
    }

    private IEnumerable<NativeSaveRecord> EnumerateRecords(string directory)
    {
        return _usesSessionLibrary
            ? EnumerateBuild91Records(directory)
            : EnumerateBuild37Records(directory);
    }

    private IEnumerable<NativeSaveRecord> EnumerateBuild91Records(string directory)
    {
        Type libraryType = AccessTools.TypeByName("GAT.SessionLibrary")
            ?? throw new TypeLoadException("Build 91 SessionLibrary was not found.");
        MethodInfo loadEntries = AccessTools.Method(libraryType, "LoadEntries", new[] { typeof(string) })
            ?? throw new MissingMethodException(libraryType.FullName, "LoadEntries");
        object? loaded = loadEntries.Invoke(null, new object[] { directory });
        if (loaded is not IEnumerable nativeEntries) throw new InvalidDataException("Build 91 returned an invalid save list.");
        foreach (object? nativeEntry in nativeEntries)
        {
            if (nativeEntry == null) continue;
            string? name = ReadStringMember(nativeEntry, "name");
            string? path = ReadStringMember(nativeEntry, "path");
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(path))
                yield return new NativeSaveRecord(name, path);
        }
    }

    private IEnumerable<NativeSaveRecord> EnumerateBuild37Records(string directory)
    {
        foreach (string path in Directory.GetFiles(directory))
        {
            if (!string.Equals(System.IO.Path.GetExtension(path), _extension, StringComparison.OrdinalIgnoreCase)) continue;
            yield return new NativeSaveRecord(System.IO.Path.GetFileNameWithoutExtension(path), path);
        }
    }

    private bool IsSelectable(NativeSaveRecord record)
    {
        return !SaveCollectionPolicy.IsInternalSaveName(record.Name) &&
            string.Equals(System.IO.Path.GetExtension(record.Path), _extension, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(System.IO.Path.GetFileNameWithoutExtension(record.Path), record.Name, StringComparison.Ordinal);
    }

    private bool TryValidateRecord(NativeSaveRecord record)
    {
        try
        {
            ReadNativeSave(record.Path);
            return true;
        }
        catch (Exception exception) when (IsExpectedSaveFailure(exception))
        {
            _log.LogWarning($"[Saves] omitted unreadable save '{record.Path}': {GetFailure(exception).Message}");
            return false;
        }
    }

    private static void ReadNativeSave(string path)
    {
        MethodInfo readFile = AccessTools.Method(typeof(WorldSerializer), "ReadFile", new[] { typeof(string) })
            ?? throw new MissingMethodException(nameof(WorldSerializer), "ReadFile");
        try
        {
            readFile.Invoke(null, new object[] { path });
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }

    private static string? ReadStringMember(object instance, string name)
    {
        object? value = AccessTools.Field(instance.GetType(), name)?.GetValue(instance)
            ?? AccessTools.Property(instance.GetType(), name)?.GetValue(instance);
        return value as string;
    }

    private static bool IsExpectedSaveFailure(Exception exception)
    {
        Exception failure = GetFailure(exception);
        return failure is IOException || failure is UnauthorizedAccessException || failure is ArgumentException;
    }

    private static Exception GetFailure(Exception exception) =>
        exception is TargetInvocationException { InnerException: not null } invocation ? invocation.InnerException : exception;

    private static int CompareEntries(SaveImportEntry left, SaveImportEntry right)
    {
        int collectionOrder = left.Collection.CompareTo(right.Collection);
        return collectionOrder != 0 ? collectionOrder : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
    }

    private sealed class NativeSaveRecord
    {
        public NativeSaveRecord(string name, string path)
        {
            Name = name;
            Path = path;
        }

        public string Name { get; }

        public string Path { get; }
    }
}
