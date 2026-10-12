using System;
using System.Reflection;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Diagnostics;

internal static class ScenarioDiagnosticPatchInstaller
{
    public static void Install(Harmony harmony, DiagnosticProbe probe)
    {
        (MethodBase target, MethodInfo postfix) = probe switch
        {
            DiagnosticProbe.Water => (AccessTools.Method(typeof(WaterManager), nameof(WaterManager.SpawnWaterAtCell)), Callback(typeof(WorldStateScenario), nameof(WorldStateScenario.ObserveWater))),
            DiagnosticProbe.Lightning => (AccessTools.Method(typeof(WeatherManager), "AnimateLightning"), Callback(typeof(WorldStateScenario), nameof(WorldStateScenario.ObserveLightning))),
            DiagnosticProbe.Animation => (AccessTools.Method(typeof(GATAnimationManager), nameof(GATAnimationManager.PlayAnimations)), Callback(typeof(WorldStateScenario), nameof(WorldStateScenario.ObserveAnimation))),
            DiagnosticProbe.DrawMarker => (AccessTools.Method(typeof(CommandVisuals), nameof(CommandVisuals.DrawHud), new[] { typeof(CommandVisuals.HudKind), typeof(Vector3) }), Callback(typeof(CommandMarkerScenario), nameof(CommandMarkerScenario.ObserveDraw))),
            DiagnosticProbe.CommandAudio => (AccessTools.Method(typeof(GATSoundManager), "PlayAudioGroupAt"), Callback(typeof(CommandAudioScenario), nameof(CommandAudioScenario.ObservePlayback))),
            _ => throw new ArgumentOutOfRangeException(nameof(probe), probe, "Unknown diagnostic probe."),
        };

        if (target == null) throw new MissingMethodException($"Diagnostic target for {probe} was not found.");
        harmony.Patch(target, postfix: new HarmonyMethod(postfix));
    }

    private static MethodInfo Callback(Type type, string name) =>
        AccessTools.Method(type, name) ?? throw new MissingMethodException(type.Name, name);
}
