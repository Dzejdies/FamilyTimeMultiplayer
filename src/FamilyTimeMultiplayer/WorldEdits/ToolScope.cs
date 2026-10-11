namespace FamilyTimeMultiplayer.WorldEdits;

public static class ToolScope
{
    public static int AxeDepth { get; set; }

    public static int MalletDepth { get; set; }

    public static bool InAxe => AxeDepth > 0;

    public static bool InMallet => MalletDepth > 0;
}
