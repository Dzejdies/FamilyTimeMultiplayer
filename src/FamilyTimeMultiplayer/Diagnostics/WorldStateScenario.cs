using System;
using System.Collections;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class WorldStateScenario : ScenarioRunner
{
    private readonly ManualLogSource _log;
    private double _start = -1;
    private int _step;
    private Handle _child;
    internal static WorldStateScenario? Active { get; private set; }
    private int _waterApplied;
    private int _lightningApplied;
    private int _animationsApplied;
    private readonly bool _initialStorm;
    private bool _stormPrepared;

    public WorldStateScenario(ManualLogSource log, bool initialStorm = false)
    {
        _log = log;
        _initialStorm = initialStorm;
        FamilyTimeMultiplayer.Game.GameApiCompatibility.SetTargetFrameRate(60);
        Active = this;
    }

    public override void Tick(double now, ISession? session)
    {
        if (_initialStorm && !_stormPrepared && session?.Role == SessionRole.Host && LocalPlayer.IsWorldReady)
        {
            _stormPrepared = true;
            WeatherManager initial = World.weatherManager!;
            SetLongStormIntervals(initial);
            initial.StartRain();
            World.gatNames!.SetName(Families.GameFamilyAccess.CurrentChildren().OrderBy(h => h.stableIndex).First(), "FTMP-initial");
            _log.LogInfo("[WorldTest] prepared named child and storm before client join");
        }
        if (session == null || !HasLivePair(session))
        {
            return;
        }
        if (_start < 0)
        {
            _start = now;
            _child = Families.GameFamilyAccess.CurrentChildren()
                .OrderBy(h => session is ClientSession client && client.Identities.TryGetHost(h.stableIndex, out ushort host) ? host : h.stableIndex)
                .First();
            if (_initialStorm && session.Role == SessionRole.Client)
            {
                WeatherManager initial = World.weatherManager!;
                Check("join during storm", initial.IsRaining && initial.IsLightningEnabled && initial.IsFlashFloodsEnabled,
                    $"rain={initial.IsRaining} lightning={initial.IsLightningEnabled} floods={initial.IsFlashFloodsEnabled}");
                Check("initial saved name", Name() == "FTMP-initial", Name());
                Check("join stops loaded weather loops", Traverse.Create(initial).Field("lightningCoroutine").GetValue() == null &&
                    Traverse.Create(initial).Field("flashFloodCoroutine").GetValue() == null, "coroutine fields");
            }
        }
        double elapsed = now - _start;
        WeatherManager weather = World.weatherManager!;
        switch (_step)
        {
            case 0 when elapsed > 4:
                _step++;
                if (session.Role == SessionRole.Host)
                {
                    SetLongStormIntervals(weather);
                    weather.StartRain();
                    World.gatNames!.SetName(_child, "FTMP-host");
                    _log.LogInfo($"[WorldTest] host renamed child {_child.stableIndex} and started rain");
                }
                break;
            case 1 when elapsed > 7:
                _step++;
                if (session.Role == SessionRole.Host)
                {
                    Vector3 position = new Vector3(17f, 160f, 19f);
                    weather.StartCoroutine((IEnumerator)AccessTools.Method(typeof(WeatherManager), "AnimateLightning").Invoke(weather, new object[] { position }));
                    World.gatAnimationManager!.PlayAnimations(new[] { _child }, AnimationEnum.EAT);
                }
                break;
            case 2 when elapsed > 11:
                _step++;
                if (session.Role == SessionRole.Client)
                {
                    Check("host name reaches client", Name() == "FTMP-host", Name());
                    Check("rain and storm reach client", weather.IsRaining && weather.IsLightningEnabled && weather.IsFlashFloodsEnabled,
                        $"rain={weather.IsRaining} lightning={weather.IsLightningEnabled} floods={weather.IsFlashFloodsEnabled}");
                    Check("client rain has visible droplets", weather.VisibleChunkCount > 0 && weather.WaterDropletCount > 0,
                        $"chunks={weather.VisibleChunkCount} droplets={weather.WaterDropletCount}");
                    AudioSource ambience = Traverse.Create(weather).Field<AudioSource>("weatherAmbienceLoop").Value;
                    Check("client rain audio is playing", ambience != null && ambience.isPlaying && ambience.volume > 0f, "weather ambience source");
                    Check("client random weather loops stopped", Traverse.Create(weather).Field("lightningCoroutine").GetValue() == null &&
                        Traverse.Create(weather).Field("flashFloodCoroutine").GetValue() == null, "coroutine fields");
                    Check("lightning reaches client", _lightningApplied > 0, $"applied={_lightningApplied}");
                    Check("animation reaches client", _animationsApplied > 0, $"applied={_animationsApplied}");
                    World.commandService!.StartleChildren(100f);
                    FollowController follow = Traverse.Create(World.commandService!).Field<FollowController>("_follow").Value;
                    Check("thunder leaves client commands passive", !follow.IsFollowing, $"localFollow={follow.IsFollowing}");
                    WaterManager water = World.waterManager!;
                    Vector2Int cell = new Vector2Int(water.WaterWindowOriginX + 128, water.WaterWindowOriginZ + 128);
                    water.SpawnWaterAtCell(cell, 0.5f);
                    World.gatNames!.SetName(_child, "FTMP-client");
                    _log.LogInfo($"[WorldTest] client water request {cell}, renamed child {_child.stableIndex}");
                }
                break;
            case 3 when elapsed > 19:
                _step++;
                Check("client name reaches host and echo", Name() == "FTMP-client", Name());
                Check("water executes on host and replicates", _waterApplied > 0, $"applied={_waterApplied}");
                if (session.Role == SessionRole.Host)
                {
                    weather.StopRain();
                }
                break;
            case 4 when elapsed > 24:
                _step++;
                Check("storm stops on both machines", !weather.IsRaining && !weather.IsLightningEnabled && !weather.IsFlashFloodsEnabled,
                    $"rain={weather.IsRaining} lightning={weather.IsLightningEnabled} floods={weather.IsFlashFloodsEnabled}");
                _log.LogInfo($"[WorldTest] DONE {session.Role}");
                break;
        }
    }

    private string Name() => World.gatNames!.TryGetName(_child, out string value) ? value : string.Empty;

    private static void SetLongStormIntervals(WeatherManager weather)
    {
        Traverse.Create(weather).Field<float>("minLightningInterval").Value = 600f;
        Traverse.Create(weather).Field<float>("maxLightningInterval").Value = 600f;
        Traverse.Create(weather).Field<float>("flashFloodMinInterval").Value = 600f;
        Traverse.Create(weather).Field<float>("flashFloodMaxInterval").Value = 600f;
    }

    private void Check(string name, bool pass, string detail)
    {
        if (pass)
        {
            _log.LogInfo($"[WorldTest] PASS {name}: {detail}");
        }
        else
        {
            _log.LogError($"[WorldTest] FAIL {name}: {detail}");
        }
    }

    [HarmonyPatch(typeof(WaterManager), nameof(WaterManager.SpawnWaterAtCell))]
    private static class WaterProbe
    {
        private static void Postfix(bool __runOriginal, bool __result)
        {
            if (Active != null && __runOriginal && __result && !ReplicationGate.BlocksLocalSimulation)
            {
                Active._waterApplied++;
            }
        }
    }

    [HarmonyPatch(typeof(WeatherManager), "AnimateLightning")]
    private static class LightningProbe
    {
        private static void Postfix(Vector3 position)
        {
            if (Active != null && ReplicationGate.Role == SessionRole.Client && position == new Vector3(17f, 160f, 19f))
            {
                Active._lightningApplied++;
            }
        }
    }

    [HarmonyPatch(typeof(GATAnimationManager), nameof(GATAnimationManager.PlayAnimations))]
    private static class AnimationProbe
    {
        private static void Postfix(Handle[] handles, AnimationEnum animationType)
        {
            if (Active != null && AnimationRelay.Applying && animationType == AnimationEnum.EAT && handles.Contains(Active._child))
            {
                Active._animationsApplied++;
            }
        }
    }
}
