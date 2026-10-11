using System.Diagnostics;

namespace FamilyTimeMultiplayer.Game;

public static class GameClock
{
    private static readonly long StartTimestamp = Stopwatch.GetTimestamp();

    public static double Now => (Stopwatch.GetTimestamp() - StartTimestamp) / (double)Stopwatch.Frequency;
}
