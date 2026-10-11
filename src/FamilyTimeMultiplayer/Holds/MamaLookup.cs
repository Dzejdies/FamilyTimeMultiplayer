using System.Collections.Generic;
using System.Linq;
using FamilyTimeMultiplayer.Families;
using FamilyTimeMultiplayer.Game;
using GAT;

namespace FamilyTimeMultiplayer.Holds;

public static class MamaLookup
{
    public static IEnumerable<WolfMama> All()
    {
        WolfMama? original = WolfMama.ActiveInstance;
        if (original != null)
        {
            yield return original;
        }
        foreach (WolfMama clone in MamaCloning.LiveClones.Where(c => c != original))
        {
            yield return clone;
        }
    }

    public static bool TryFind(ushort agent, out WolfMama mama)
    {
        mama = All().FirstOrDefault(m => !m.FakeGATHandle.isNull && m.FakeGATHandle.stableIndex == agent)!;
        return mama != null;
    }

    public static bool TryFind(RigidTransform rigid, out WolfMama mama)
    {
        mama = All().FirstOrDefault(candidate => RigidTransformApi.FromMama(candidate) == rigid)!;
        return mama != null;
    }

    public static bool IsClone(WolfMama mama) => MamaCloning.LiveClones.Contains(mama);
}
