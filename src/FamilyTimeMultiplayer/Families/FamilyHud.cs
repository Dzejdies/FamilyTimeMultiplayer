using System;
using System.Reflection;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Families;

public readonly struct FamilyHudCounts : IEquatable<FamilyHudCounts>
{
    public FamilyHudCounts(int children, int hungry, int notSleeping)
    {
        Children = children;
        Hungry = hungry;
        NotSleeping = notSleeping;
    }

    public int Children { get; }
    public int Hungry { get; }
    public int NotSleeping { get; }
    public int Sleeping => Math.Max(0, Children - NotSleeping);

    public bool Equals(FamilyHudCounts other) =>
        Children == other.Children && Hungry == other.Hungry && NotSleeping == other.NotSleeping;

    public override bool Equals(object? obj) => obj is FamilyHudCounts other && Equals(other);

    public override int GetHashCode() => (Children * 397 ^ Hungry) * 397 ^ NotSleeping;
}

public static class FamilyHud
{
    private static readonly MethodInfo RefreshDisplay =
        Game.GameApiCompatibility.HudRefreshMethod;

    private static readonly MethodInfo RefreshTotalPlayerCount =
        AccessTools.Method(typeof(GATStateDisplayUI), "RefreshTotalPlayerCount")
        ?? throw new MissingMethodException(nameof(GATStateDisplayUI), "RefreshTotalPlayerCount");

    public static FamilyHudCounts? Override { get; private set; }

    internal static bool DisplayRefreshing { get; set; }

    public static void Set(FamilyHudCounts counts)
    {
        if (Override.HasValue && Override.Value.Equals(counts))
        {
            return;
        }
        Override = counts;
        Invoke(RefreshDisplay);
    }

    public static void Refresh() => Invoke(RefreshDisplay);

    public static void Clear()
    {
        if (!Override.HasValue)
        {
            return;
        }
        Override = null;
        Invoke(RefreshTotalPlayerCount);
    }

    public static FamilyHudCounts ReadDisplayedCounts()
    {
        GATStateDisplayUI display = Game.GatInterop.RequireAlive(World.stateDisplayUI, "World.stateDisplayUI is missing.");
        Traverse fields = Traverse.Create(display);
        return new FamilyHudCounts(fields.Field<int>("_totalPlayerCount").Value, display.GetHungryCount(), display.GetAgentsNotSleepingCount());
    }

    private static void Invoke(MethodInfo method)
    {
        GATStateDisplayUI? display = World.stateDisplayUI;
        if (display != null && display.isActiveAndEnabled)
        {
            method.Invoke(display, null);
        }
    }
}
