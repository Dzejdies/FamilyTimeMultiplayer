using System;

namespace FamilyTimeMultiplayer.Core.Families;

public readonly struct FamilyNightState
{
    public FamilyNightState(int children, int notSleeping, int hungry)
    {
        if (children < 0 || notSleeping < 0 || hungry < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(children), "Family counts cannot be negative.");
        }
        Children = children;
        NotSleeping = Math.Min(notSleeping, children);
        Hungry = Math.Min(hungry, children);
    }

    public int Children { get; }
    public int NotSleeping { get; }
    public int Hungry { get; }
}

public readonly struct FamilyNightOutcome
{
    public FamilyNightOutcome(bool childrenMissing, bool childrenHungry, int grownBy)
    {
        ChildrenMissing = childrenMissing;
        ChildrenHungry = childrenHungry;
        GrownBy = grownBy;
    }

    public bool ChildrenMissing { get; }
    public bool ChildrenHungry { get; }
    public int GrownBy { get; }
    public bool Happy => !ChildrenMissing && !ChildrenHungry;
}

public static class NightRules
{
    public static FamilyNightOutcome Evaluate(FamilyNightState state, float growthMultiplier)
    {
        if (float.IsNaN(growthMultiplier) || growthMultiplier < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(growthMultiplier), growthMultiplier, "Growth multiplier must be a non-negative number.");
        }
        bool missing = state.NotSleeping > 0;
        bool hungry = state.Hungry > 0;
        int grownBy = !missing && !hungry && state.Children > 0
            ? (int)Math.Ceiling(state.Children * Math.Max(1f, growthMultiplier))
            : 0;
        return new FamilyNightOutcome(missing, hungry, grownBy);
    }
}
