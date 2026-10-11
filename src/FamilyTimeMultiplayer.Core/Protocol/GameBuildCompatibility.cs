using System;
using System.Collections.Generic;

namespace FamilyTimeMultiplayer.Core.Protocol;

public static class GameBuildCompatibility
{
    private static readonly HashSet<string> Build91Artifacts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "7ddcd8f6e25c4b7a9d21e4d54ebc8d90",
        "49296d0c647547449c6e7119d4ecdf5b",
        "62d864033bfd4caeb6e1ba96959d0b1b"
    };

    public static bool AreCompatible(string hostBuildId, string clientBuildId)
    {
        if (string.Equals(hostBuildId, clientBuildId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Build91Artifacts.Contains(hostBuildId) && Build91Artifacts.Contains(clientBuildId);
    }
}
