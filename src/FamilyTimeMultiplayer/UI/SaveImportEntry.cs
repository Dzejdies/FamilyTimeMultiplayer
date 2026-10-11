using FamilyTimeMultiplayer.Core.Saves;

namespace FamilyTimeMultiplayer.UI;

internal sealed class SaveImportEntry
{
    public SaveImportEntry(SaveCollection collection, string name, string path)
    {
        Collection = collection;
        Name = name;
        Path = path;
    }

    public SaveCollection Collection { get; }

    public string Name { get; }

    public string Path { get; }

    public string Label => $"{Collection} — {Name}";
}
