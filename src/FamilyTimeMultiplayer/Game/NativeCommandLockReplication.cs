using System;
using GAT;

namespace FamilyTimeMultiplayer.Game;

internal static class NativeCommandLockReplication
{
    internal static bool Read(ushort index) => RequireOwnership().IsCommandLocked(GatInterop.ToHandle(index));

    internal static void Apply(ushort index, bool locked)
    {
        if (!RequireOwnership().SetCommandLocked(GatInterop.ToHandle(index), locked))
            throw new InvalidOperationException($"Native command ledger refused replicated child lock {index}.");
        GameApiCompatibility.InvalidateNativeCommandLocks();
    }

    private static CommandOwnershipService RequireOwnership() =>
        GatInterop.RequireAlive(World.commandOwnershipService, "Native command ownership service is missing.");
}
