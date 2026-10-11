namespace FamilyTimeMultiplayer.UI;

public static class StartupGate
{
    public static bool IsAwaitingChoice { get; private set; }

    public static bool IsHolding { get; private set; }

    public static void Hold()
    {
        IsAwaitingChoice = true;
        IsHolding = true;
    }

    public static void ChoiceMade() => IsAwaitingChoice = false;

    public static void Release()
    {
        IsAwaitingChoice = false;
        IsHolding = false;
    }
}
