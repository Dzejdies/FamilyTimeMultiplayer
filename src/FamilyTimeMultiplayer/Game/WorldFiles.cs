using FamilyTimeMultiplayer.Core.Saves;

namespace FamilyTimeMultiplayer.Game;

public static class WorldFiles
{
    public const string HostSnapshotMapName = SaveCollectionPolicy.HostSnapshotName;
    public const string ClientReceivedMapName = SaveCollectionPolicy.ClientReceivedWorldName;

    public static string PathFor(string mapName) => SaveCollectionService.RequireCurrent().GetWorldPath(mapName);
}
