using UnityEngine;

namespace FamilyTimeMultiplayer.Avatars;

public sealed class PlayerAvatarAnimator
{
    private const float PositionSharpness = 14f;
    private const float YawSharpness = 10f;
    private const float SnapDistance = 6f;
    private const float StrideLength = 1.3f;
    private const float FullSwingSpeed = 4f;
    private const float MaxLegSwingDegrees = 34f;
    private const float MaxArmSwingDegrees = 24f;
    private const float StepBobHeight = 0.035f;
    private const float SpeedSharpness = 8f;

    private readonly PlayerAvatarRig _rig;
    private Vector3 _position;
    private float _yaw;
    private float _speed;
    private float _phase;
    private float _idleTime;
    private bool _placed;

    public PlayerAvatarAnimator(PlayerAvatarRig rig)
    {
        _rig = rig;
    }

    public void Advance(Vector3 targetPosition, float targetYawDegrees, float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return;
        }
        Vector3 previous = _position;
        if (!_placed || (targetPosition - _position).sqrMagnitude > SnapDistance * SnapDistance)
        {
            _position = targetPosition;
            _yaw = targetYawDegrees;
            previous = targetPosition;
            _placed = true;
        }
        else
        {
            _position = Vector3.Lerp(_position, targetPosition, 1f - Mathf.Exp(-PositionSharpness * deltaTime));
            _yaw = AvatarMath.LerpAngle(_yaw, targetYawDegrees, 1f - Mathf.Exp(-YawSharpness * deltaTime));
        }
        Vector3 displacement = _position - previous;
        displacement.y = 0f;
        float instantSpeed = displacement.magnitude / deltaTime;
        _speed = Mathf.Lerp(_speed, instantSpeed, 1f - Mathf.Exp(-SpeedSharpness * deltaTime));
        _phase = AvatarMath.Repeat(_phase + _speed / StrideLength * AvatarMath.Pi * deltaTime, AvatarMath.FullCircleRadians);
        _idleTime += deltaTime;
        Pose();
    }

    private void Pose()
    {
        float gait = Mathf.Clamp01(_speed / FullSwingSpeed);
        float swing = Mathf.Sin(_phase);
        float legAngle = swing * MaxLegSwingDegrees * gait;
        float armAngle = swing * MaxArmSwingDegrees * gait;
        _rig.Root.transform.SetPositionAndRotation(_position, Quaternion.Euler(0f, _yaw, 0f));
        _rig.Body.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Cos(_phase)) * StepBobHeight * gait, 0f);
        _rig.LeftLeg.localRotation = Quaternion.Euler(legAngle, 0f, 0f);
        _rig.RightLeg.localRotation = Quaternion.Euler(-legAngle, 0f, 0f);
        _rig.LeftArm.localRotation = Quaternion.Euler(-armAngle, 0f, -6f);
        _rig.RightArm.localRotation = Quaternion.Euler(armAngle, 0f, 6f);
        float breathe = 1f + Mathf.Sin(_idleTime * 1.6f) * 0.012f * (1f - gait);
        _rig.Torso.localScale = new Vector3(breathe, 1f, breathe);
    }
}
