using System;

namespace FamilyTimeMultiplayer.Avatars;

internal static class AvatarMath
{
    internal const float Pi = 3.14159265358979323846f;
    internal const float FullCircleRadians = Pi * 2f;
    private const float HalfCircleDegrees = 180f;
    private const float FullCircleDegrees = 360f;

    internal static float LerpAngle(float current, float target, float amount)
    {
        float difference = Repeat(target - current + HalfCircleDegrees, FullCircleDegrees) - HalfCircleDegrees;
        return current + difference * Math.Max(0f, Math.Min(1f, amount));
    }

    internal static float Repeat(float value, float length) => value - (float)Math.Floor(value / length) * length;
}
