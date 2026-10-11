using FamilyTimeMultiplayer.Core.Protocol;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.Game;

public static class LocalPlayer
{
    public static bool IsWorldReady
    {
        get
        {
            WorldSerializer? serializer = World.worldSerializer;
            PlayerController? controller = World.playerController;
            return World.gat != null
                && serializer != null
                && !serializer.IsBusy
                && !serializer.isLoadingInProgress
                && controller != null
                && controller.IsPossessing;
        }
    }

    public static Handle PossessedHandle =>
        World.playerController != null ? World.playerController.PossessedHandle : Handle.Null;

    public static Vector3 Position =>
        GatInterop.RequireAlive(World.playerController, "World.playerController is missing.").CachedInstancePosition;

    public static float YawDegrees
    {
        get
        {
            Camera? camera = Camera.main;
            return camera != null ? camera.transform.eulerAngles.y : 0f;
        }
    }

    public static float PitchDegrees
    {
        get
        {
            Camera? camera = Camera.main;
            if (camera == null)
            {
                return 0f;
            }
            float pitch = camera.transform.eulerAngles.x;
            return pitch > 180f ? pitch - 360f : pitch;
        }
    }

    public static void Possess(Handle handle)
    {
        PlayerController controller = GatInterop.RequireAlive(World.playerController, "World.playerController is missing.");
        if (World.gat == null || !World.gat.IsHandleValid(handle))
        {
            throw new System.InvalidOperationException($"Cannot possess {handle}: it is not a live GAT instance.");
        }
        controller.Possess(handle);
        if (controller.PossessedHandle != handle)
        {
            throw new System.InvalidOperationException($"PlayerController refused to possess {handle}.");
        }
    }
}

public sealed class LocalPoseTracker
{
    private Vector3 _lastPosition;
    private double _lastTime = -1;

    public void Reset() => _lastTime = -1;

    public ClientPoseMessage Sample(double now)
    {
        Vector3 position = LocalPlayer.Position;
        Vector3 velocity = Vector3.zero;
        if (_lastTime >= 0 && now > _lastTime)
        {
            velocity = (position - _lastPosition) / (float)(now - _lastTime);
        }
        _lastPosition = position;
        _lastTime = now;
        return new ClientPoseMessage(GatInterop.ToNet(position), LocalPlayer.YawDegrees, LocalPlayer.PitchDegrees, GatInterop.ToNet(velocity));
    }
}
