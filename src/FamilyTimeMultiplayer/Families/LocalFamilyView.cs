using GAT;
using System.Collections.Generic;
using System.Linq;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;

namespace FamilyTimeMultiplayer.Families;

public static class LocalFamilyView
{
    private static ushort[] _members = System.Array.Empty<ushort>();

    public static void SetMembers(ushort[]? members) => _members = members ?? System.Array.Empty<ushort>();
    public static void Clear() => _members = System.Array.Empty<ushort>();

    public static bool FiltersIcons => MultiplayerController.Current?.Session switch
    {
        HostSession host => host.FamilyMode == FamilyMode.Separate,
        ClientSession client => client.FamilyMode == FamilyMode.Separate,
        _ => false,
    };

    public static int[] PhysicalMembers()
    {
        if (World.gat == null) return System.Array.Empty<int>();
        IEnumerable<ushort> local;
        if (MultiplayerController.Current?.Session is ClientSession client)
        {
            local = _members.Select(id => client.Identities.TryGetLocal(id, out ushort mapped) ? mapped : ushort.MaxValue);
        }
        else
        {
            HostFamilyService? families = HostFamilyService.Current;
            local = families == null ? System.Array.Empty<ushort>() : families.MembersOf(families.HostOwner);
        }
        return local.Where(id => id != ushort.MaxValue)
            .Select(id => (int)World.gat.ResolveHandle(GatInterop.ToHandle(id)))
            .Where(id => id < World.gat.instanceCount).Distinct().OrderBy(id => id).ToArray();
    }
}
