using UnityEngine;

namespace FamilyTimeMultiplayer.UI;

public static class PlayerNotice
{
    private const float DefaultSeconds = 3.5f;

    public static string? Text { get; private set; }

    public static float Until { get; private set; }

    public static string? Sticky { get; private set; }

    public static void Show(string text, float seconds = DefaultSeconds)
    {
        Text = text;
        Until = Time.unscaledTime + seconds;
    }

    public static void SetSticky(string? text) => Sticky = text;

    public static string? Current =>
        Text != null && Time.unscaledTime < Until ? Text : Sticky;

    public static void Clear()
    {
        Text = null;
        Sticky = null;
    }
}
