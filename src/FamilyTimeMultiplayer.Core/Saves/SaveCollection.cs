using System;
using System.IO;

namespace FamilyTimeMultiplayer.Core.Saves;

public enum SaveCollection
{
    Solo,
    Multiplayer
}

public sealed class SaveCollectionSelection
{
    public SaveCollection Current { get; private set; } = SaveCollection.Solo;

    public void Select(SaveCollection collection)
    {
        if (!Enum.IsDefined(typeof(SaveCollection), collection)) throw new ArgumentOutOfRangeException(nameof(collection));
        Current = collection;
    }
}

public static class SaveCollectionPaths
{
    public static string GetDirectory(string soloDirectory, SaveCollection collection, string multiplayerDirectoryName)
    {
        ValidateRoot(soloDirectory);
        if (collection == SaveCollection.Solo) return Path.GetFullPath(soloDirectory);
        if (collection != SaveCollection.Multiplayer) throw new ArgumentOutOfRangeException(nameof(collection));
        ValidateDirectoryName(multiplayerDirectoryName);
        return Path.Combine(Path.GetFullPath(soloDirectory), multiplayerDirectoryName);
    }

    private static void ValidateRoot(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathRooted(directory))
            throw new ArgumentException("The native save directory must be an absolute path.", nameof(directory));
    }

    private static void ValidateDirectoryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == "." || name == ".." ||
            name.IndexOfAny(new[] { '/', '\\' }) >= 0 || Path.GetFileName(name) != name)
            throw new ArgumentException("The multiplayer save directory must be a single path segment.", nameof(name));
    }
}

public static class SaveCollectionPolicy
{
    public const string HostSnapshotName = "ftmp_host_snapshot";
    public const string ClientReceivedWorldName = "ftmp_from_host";

    public static bool IsInternalSaveName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string saveName = Path.GetFileNameWithoutExtension(name);
        return string.Equals(saveName, HostSnapshotName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(saveName, ClientReceivedWorldName, StringComparison.OrdinalIgnoreCase);
    }
}
