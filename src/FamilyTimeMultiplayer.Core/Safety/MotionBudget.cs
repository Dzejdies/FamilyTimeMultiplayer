using System;
using FamilyTimeMultiplayer.Core.Protocol;

namespace FamilyTimeMultiplayer.Core.Safety;

public sealed class MotionBudget
{
    private readonly float _speed;
    private readonly float _capacity;
    private double _time;
    private float _credit;
    private float _verticalCredit;
    public NetVector3 Position { get; private set; }

    public MotionBudget(NetVector3 origin, double now, float speed, float slack = 3)
    {
        Position = origin;
        _time = now;
        _speed = speed;
        _capacity = speed * 2 + slack;
        _credit = slack;
        _verticalCredit = slack;
    }

    public bool TryMove(NetVector3 position, double now)
    {
        if (!InWorld(position) || !double.IsFinite(now) || now < _time) return false;
        float credit = Math.Min(_capacity, _credit + (float)(now - _time) * _speed);
        NetVector3 delta = position - Position;
        float distance = MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        float vertical = Math.Min(200, _verticalCredit + (float)(now - _time) * 100);
        if (distance > credit || Math.Abs(delta.Y) > vertical) return false;
        _verticalCredit = vertical - Math.Abs(delta.Y);
        _credit = credit - distance;
        _time = now;
        Position = position;
        return true;
    }

    public static bool InWorld(NetVector3 point) => point.IsFinite && point.X >= -2048 && point.X < 2048 && point.Z >= -2048 && point.Z < 2048 && point.Y >= -16 && point.Y <= 512;
    public static bool Near(NetVector3 origin, NetVector3 target, float reach) => InWorld(target) && (target - origin).Magnitude <= reach;
    public static bool ValidPose(ClientPoseMessage pose) => InWorld(pose.Position) && pose.Velocity.IsFinite && pose.Velocity.Magnitude <= 100 && float.IsFinite(pose.YawDegrees) && Math.Abs(pose.YawDegrees) <= 360 && float.IsFinite(pose.PitchDegrees) && Math.Abs(pose.PitchDegrees) <= 90;
    public static bool ValidHold(HoldMessage hold) => InWorld(hold.Position) && hold.Velocity.IsFinite && hold.Velocity.Magnitude <= 100 && ValidRotation(hold.Rotation);
    public static bool ValidRotation(NetQuaternion q)
    {
        float length = q.X*q.X + q.Y*q.Y + q.Z*q.Z + q.W*q.W;
        return float.IsFinite(length) && Math.Abs(length - 1) <= 0.02f;
    }
}

public sealed class WorkBudget
{
    private double _time;
    private double _credit = 8192;
    public bool TrySpend(int cost, double now)
    {
        if (cost < 0 || !double.IsFinite(now) || now < _time) return false;
        _credit = Math.Min(8192, _credit + (now - _time) * 4096);
        _time = now;
        if (cost > _credit) return false;
        _credit -= cost;
        return true;
    }
}
