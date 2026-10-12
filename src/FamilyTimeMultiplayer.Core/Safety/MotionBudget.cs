using System;
using FamilyTimeMultiplayer.Core.Protocol;

namespace FamilyTimeMultiplayer.Core.Safety;

public enum MotionMoveRejection
{
    None,
    InvalidPosition,
    InvalidTimestamp,
    TimestampReversed,
    HorizontalDistanceExceeded,
    VerticalDistanceExceeded,
}

public enum MotionPoseRejection
{
    None,
    MissingPlayerAuthority,
    InvalidPose,
    PositionBudgetExceeded,
}

public readonly struct MotionMoveDiagnostics
{
    public MotionMoveDiagnostics(
        MotionMoveRejection rejection,
        NetVector3 origin,
        NetVector3 target,
        double elapsedSeconds,
        float horizontalDistance,
        float horizontalAllowance,
        float verticalDistance,
        float verticalAllowance)
    {
        Rejection = rejection;
        Origin = origin;
        Target = target;
        ElapsedSeconds = elapsedSeconds;
        HorizontalDistance = horizontalDistance;
        HorizontalAllowance = horizontalAllowance;
        VerticalDistance = verticalDistance;
        VerticalAllowance = verticalAllowance;
    }

    public MotionMoveRejection Rejection { get; }
    public NetVector3 Origin { get; }
    public NetVector3 Target { get; }
    public double ElapsedSeconds { get; }
    public float HorizontalDistance { get; }
    public float HorizontalAllowance { get; }
    public float VerticalDistance { get; }
    public float VerticalAllowance { get; }
    public bool Accepted => Rejection == MotionMoveRejection.None;

    public NetVector3 GetAuthorizedVelocity(float horizontalSpeedLimit)
    {
        if (!float.IsFinite(horizontalSpeedLimit) || horizontalSpeedLimit < 0)
            throw new ArgumentOutOfRangeException(nameof(horizontalSpeedLimit));
        if (!double.IsFinite(ElapsedSeconds) || ElapsedSeconds <= 0) return NetVector3.Zero;

        NetVector3 displacement = Target - Origin;
        float inverseElapsed = 1f / (float)ElapsedSeconds;
        float x = displacement.X * inverseElapsed;
        float y = displacement.Y * inverseElapsed;
        float z = displacement.Z * inverseElapsed;
        float horizontalSpeed = MathF.Sqrt(x * x + z * z);
        if (horizontalSpeed > horizontalSpeedLimit && horizontalSpeed > 0)
        {
            float scale = horizontalSpeedLimit / horizontalSpeed;
            x *= scale;
            z *= scale;
        }
        return new NetVector3(x, y, z);
    }
}

public readonly struct MotionPoseAssessment
{
    public MotionPoseAssessment(
        MotionPoseRejection rejection,
        MotionMoveDiagnostics movement,
        float horizontalSpeed,
        float horizontalSpeedLimit)
    {
        Rejection = rejection;
        Movement = movement;
        HorizontalSpeed = horizontalSpeed;
        HorizontalSpeedLimit = horizontalSpeedLimit;
    }

    public MotionPoseRejection Rejection { get; }
    public MotionMoveDiagnostics Movement { get; }
    public float HorizontalSpeed { get; }
    public float HorizontalSpeedLimit { get; }
    public bool Accepted => Rejection == MotionPoseRejection.None;
}

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
        MotionMoveDiagnostics assessment = AssessMove(position, now);
        if (!assessment.Accepted) return false;
        _verticalCredit = assessment.VerticalAllowance - assessment.VerticalDistance;
        _credit = assessment.HorizontalAllowance - assessment.HorizontalDistance;
        _time = now;
        Position = position;
        return true;
    }

    public MotionMoveDiagnostics AssessMove(NetVector3 position, double now)
    {
        double elapsed = double.IsFinite(now) ? now - _time : double.NaN;
        NetVector3 delta = position - Position;
        float horizontalDistance = MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        float verticalDistance = Math.Abs(delta.Y);
        float horizontalAllowance = 0;
        float verticalAllowance = 0;

        MotionMoveRejection rejection = !InWorld(position)
            ? MotionMoveRejection.InvalidPosition
            : !double.IsFinite(now)
                ? MotionMoveRejection.InvalidTimestamp
                : now < _time
                    ? MotionMoveRejection.TimestampReversed
                    : MotionMoveRejection.None;

        if (rejection == MotionMoveRejection.None)
        {
            horizontalAllowance = Math.Min(_capacity, _credit + (float)elapsed * _speed);
            verticalAllowance = Math.Min(200, _verticalCredit + (float)elapsed * 100);
            if (horizontalDistance > horizontalAllowance) rejection = MotionMoveRejection.HorizontalDistanceExceeded;
            else if (verticalDistance > verticalAllowance) rejection = MotionMoveRejection.VerticalDistanceExceeded;
        }

        return new MotionMoveDiagnostics(
            rejection,
            Position,
            position,
            elapsed,
            horizontalDistance,
            horizontalAllowance,
            verticalDistance,
            verticalAllowance);
    }

    public MotionPoseAssessment AssessPose(ClientPoseMessage pose, double now, float horizontalSpeedLimit)
    {
        MotionMoveDiagnostics movement = AssessMove(pose.Position, now);
        float horizontalSpeed = MathF.Sqrt(pose.Velocity.X * pose.Velocity.X + pose.Velocity.Z * pose.Velocity.Z);
        MotionPoseRejection rejection = !ValidPose(pose)
            ? MotionPoseRejection.InvalidPose
            : movement.Accepted
                ? MotionPoseRejection.None
                : MotionPoseRejection.PositionBudgetExceeded;
        return new MotionPoseAssessment(rejection, movement, horizontalSpeed, horizontalSpeedLimit);
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
