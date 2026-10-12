using System.Globalization;
using System.Text;

namespace FamilyTimeMultiplayer.RuntimeHarness.Contracts;

public static class PupObservation
{
    public static string FormatCandidateDistance(bool isValid, bool hasCamera, bool hasCandidatePosition, double distance)
    {
        if (!isValid || !hasCamera || !hasCandidatePosition) return "unavailable";
        return distance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static bool ShouldLogReadiness(string? previous, string current)
    {
        return !string.Equals(previous, current, System.StringComparison.Ordinal);
    }

    public static string ReadinessLogKey(string readiness)
    {
        var key = new StringBuilder();
        foreach (string observation in readiness.Split(';'))
        {
            if (observation.StartsWith("candidateHead=", System.StringComparison.Ordinal)) continue;
            if (observation.StartsWith("distance=", System.StringComparison.Ordinal))
            {
                key.Append("distanceRange=").Append(DistanceRange(observation[9..])).Append(';');
                continue;
            }
            key.Append(observation).Append(';');
        }
        return key.ToString();
    }

    private static string DistanceRange(string value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double distance)
            ? distance <= 3 ? "in-range" : "out-of-range"
            : "unavailable";
    }
}
