using System;
using System.Collections;
using System.Reflection;
using FamilyTimeMultiplayer.Core.Protocol;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Game;

public sealed class WeatherStateBridge
{
    private static readonly AccessTools.FieldRef<WeatherManager, bool> LightningEnabled =
        AccessTools.FieldRefAccess<WeatherManager, bool>("enableLightning");
    private static readonly AccessTools.FieldRef<WeatherManager, bool> FloodsEnabled =
        AccessTools.FieldRefAccess<WeatherManager, bool>("enableFlashFloods");
    private static readonly MethodInfo AnimateLightning =
        AccessTools.Method(typeof(WeatherManager), "AnimateLightning")
        ?? throw new MissingMethodException(nameof(WeatherManager), "AnimateLightning");

    private byte? _lastSent;
    private double _next;

    public static WorldEdit Capture()
    {
        WeatherManager weather = RequireWeather();
        return WorldEdit.Weather(weather.IsRaining, weather.IsLightningEnabled, weather.IsFlashFloodsEnabled);
    }

    public void Tick(double now)
    {
        if (now < _next || !WorldEdits.WorldEditRelay.HostRecords || World.weatherManager == null)
        {
            return;
        }
        _next = now + 1;
        WorldEdit state = Capture();
        if (_lastSent != state.Height)
        {
            _lastSent = state.Height;
            WorldEdits.WorldEditRelay.Record(state);
        }
    }

    public static void Apply(WorldEdit state)
    {
        WeatherManager weather = RequireWeather();
        if (weather.IsRaining != state.Raining)
        {
            if (state.Raining)
            {
                weather.StartRain();
            }
            else
            {
                weather.StopRain();
            }
        }
        if (state.LightningOn)
        {
            weather.StartLightning();
        }
        else
        {
            weather.StopLightning();
        }
        if (state.FloodsOn)
        {
            weather.StartFlashFloods();
        }
        else
        {
            weather.StopFlashFloods();
        }
    }

    public static void SuppressClientLoops()
    {
        WeatherManager weather = RequireWeather();
        bool lightning = weather.IsLightningEnabled;
        bool floods = weather.IsFlashFloodsEnabled;
        weather.StopLightning();
        weather.StopFlashFloods();
        LightningEnabled(weather) = lightning;
        FloodsEnabled(weather) = floods;
    }

    public static bool StartClientLightning(WeatherManager weather)
    {
        if (ReplicationGate.Role != SessionRole.Client)
        {
            return true;
        }
        LightningEnabled(weather) = true;
        return false;
    }

    public static bool StartClientFloods(WeatherManager weather)
    {
        if (ReplicationGate.Role != SessionRole.Client)
        {
            return true;
        }
        FloodsEnabled(weather) = true;
        return false;
    }

    public static void PlayLightning(NetVector3 position)
    {
        WeatherManager weather = RequireWeather();
        weather.StartCoroutine((IEnumerator)AnimateLightning.Invoke(weather, new object[] { GatInterop.ToUnity(position) }));
    }

    private static WeatherManager RequireWeather() =>
        GatInterop.RequireAlive(World.weatherManager, "World.weatherManager is missing.");
}
