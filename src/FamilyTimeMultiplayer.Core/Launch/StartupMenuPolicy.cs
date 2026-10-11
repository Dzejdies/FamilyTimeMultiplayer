namespace FamilyTimeMultiplayer.Core.Launch;

public static class StartupMenuPolicy
{
    public static bool ShouldShowStartupMenu(bool nativeTitleAvailable, bool menuEnabled, bool normalLaunch) =>
        !nativeTitleAvailable && menuEnabled && normalLaunch;
}
