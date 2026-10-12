namespace FamilyTimeMultiplayer.RuntimeHarness.Contracts;

public static class VisibilityCaptureTrigger
{
    public static int GetSample(bool controlPressed, bool shiftPressed, bool f8Pressed, bool f10Pressed)
    {
        if (!controlPressed || !shiftPressed) return 0;
        if (f8Pressed) return 1;
        return f10Pressed ? 2 : 0;
    }
}
