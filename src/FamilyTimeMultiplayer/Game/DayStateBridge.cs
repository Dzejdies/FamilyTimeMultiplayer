using System;
using System.Reflection;
using FamilyTimeMultiplayer.Core.Protocol;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Game;

public sealed class DayStateBridge
{
    private static readonly MethodInfo ApplyCurrentTimeOfDay =
        AccessTools.Method(typeof(DayManager), "ApplyCurrentTimeOfDay")
        ?? throw new MissingMethodException(nameof(DayManager), "ApplyCurrentTimeOfDay");

    public DayStateMessage Capture()
    {
        Traverse day = Traverse.Create(RequireDayManager());
        return new DayStateMessage(
            day.Field<int>("day").Value,
            day.Field<float>("timer").Value,
            day.Field<float>("startTimeOfDay").Value,
            day.Field<float>("endTimeOfDay").Value,
            day.Field<bool>("isMoving").Value,
            World.gatHungerBehavior.HungerType == HungerType.THIRST ? DailyNeed.Water : DailyNeed.Food);
    }

    public void Apply(DayStateMessage state)
    {
        if (!float.IsFinite(state.Timer) || !float.IsFinite(state.StartTimeOfDay) || !float.IsFinite(state.EndTimeOfDay) || state.Day < 0)
        {
            throw new ArgumentException("Day state from host contains invalid values.", nameof(state));
        }
        DayManager manager = RequireDayManager();
        Traverse day = Traverse.Create(manager);
        day.Field<int>("day").Value = state.Day;
        day.Field<float>("timer").Value = state.Timer;
        day.Field<float>("startTimeOfDay").Value = state.StartTimeOfDay;
        day.Field<float>("endTimeOfDay").Value = state.EndTimeOfDay;
        day.Field<bool>("isMoving").Value = state.IsMoving;
        ApplyCurrentTimeOfDay.Invoke(manager, null);
        World.gatHungerBehavior.SetHungerType(state.Need == DailyNeed.Water ? HungerType.THIRST : HungerType.FOOD);
    }

    private static DayManager RequireDayManager() =>
        GatInterop.RequireAlive(World.dayManager, "World.dayManager is missing.");
}
