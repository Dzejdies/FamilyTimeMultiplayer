using System;

namespace FamilyTimeMultiplayer.Diagnostics;

public static class ScenarioReadiness
{
    public static bool RequiresFollowerBootstrap(bool separateFamilyScenario, int followerCount) =>
        !separateFamilyScenario && followerCount < 2;

    public static bool HasTimedOut(double elapsedSeconds, double timeoutSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        }
        if (!double.IsFinite(timeoutSeconds) || timeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }
        return elapsedSeconds >= timeoutSeconds;
    }

    public static int GridHeight(byte rawHeight) => (int)Math.Round(rawHeight * 0.5, MidpointRounding.ToEven);
}
