namespace FamilyTimeMultiplayer.RuntimeHarness.Contracts;

public static class RuntimeHarnessReadinessTimeouts
{
    public const double DefaultSeconds = 90;
    public const double WorldStartupSeconds = 120;

    public static double ForCondition(string condition)
    {
        return condition == "world-ready" ? WorldStartupSeconds : DefaultSeconds;
    }
}
