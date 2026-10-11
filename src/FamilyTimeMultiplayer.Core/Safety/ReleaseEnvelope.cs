using System;
using FamilyTimeMultiplayer.Core.Protocol;

namespace FamilyTimeMultiplayer.Core.Safety;

public static class ReleaseEnvelope
{
    public static bool Accepts(NetVector3 origin, NetVector3 position, NetVector3 velocity, double elapsed, float throwSpeed)
    {
        if (!double.IsFinite(elapsed) || elapsed < 0 || elapsed > 8 || !MotionBudget.InWorld(position) || !velocity.IsFinite) return false;
        float seconds = (float)elapsed;
        NetVector3 delta = position - origin;
        float horizontal = MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        float horizontalSpeed = MathF.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
        return horizontal <= 5 + (throwSpeed + 2) * seconds && horizontalSpeed <= throwSpeed + 5 &&
            delta.Y <= 5 + throwSpeed * seconds && -delta.Y <= 5 + throwSpeed * seconds + 5 * seconds * seconds &&
            velocity.Magnitude <= throwSpeed + 5 + 10 * seconds;
    }
}
