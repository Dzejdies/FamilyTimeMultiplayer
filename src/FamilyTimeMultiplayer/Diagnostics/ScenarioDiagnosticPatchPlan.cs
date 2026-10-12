using System.Collections.Generic;

namespace FamilyTimeMultiplayer.Diagnostics;

internal enum DiagnosticProbe
{
    Water,
    Lightning,
    Animation,
    DrawMarker,
    CommandAudio,
}

internal static class ScenarioDiagnosticPatchPlan
{
    private static readonly DiagnosticProbe[] WorldState =
    {
        DiagnosticProbe.Water,
        DiagnosticProbe.Lightning,
        DiagnosticProbe.Animation,
    };

    public static IReadOnlyList<DiagnosticProbe> ForScenario(string scenario) => scenario switch
    {
        "mp-world" or "mp-sync" => WorldState,
        "mp-markers" => new[] { DiagnosticProbe.DrawMarker },
        "mp-audio" => new[] { DiagnosticProbe.CommandAudio },
        "mp-audio-weather" => new[] { DiagnosticProbe.Water, DiagnosticProbe.Lightning, DiagnosticProbe.Animation, DiagnosticProbe.CommandAudio },
        _ => new DiagnosticProbe[0],
    };
}
