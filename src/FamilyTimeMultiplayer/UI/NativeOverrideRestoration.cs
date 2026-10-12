using System.Collections.Generic;

namespace FamilyTimeMultiplayer.UI;

internal static class NativeOverrideRestoration
{
    public static bool ShouldRestore<T>(T currentValue, T appliedValue) =>
        EqualityComparer<T>.Default.Equals(currentValue, appliedValue);
}
