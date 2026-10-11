using System;
using FamilyTimeMultiplayer.Core.Protocol;
using GAT;
using UnityEngine;
using GatSimulation = global::GAT.GAT;

namespace FamilyTimeMultiplayer.Game;

public sealed class RemoteAvatarService
{
    private static readonly Vector3 SpawnOffsetFromHost = new Vector3(2f, 0f, 0f);

    public void Spawn(Action<Handle> onSpawned, Action<string> onFailed)
    {
        GatSimulation gat = GatInterop.RequireGat();
        PlayerController host = GatInterop.RequireAlive(World.playerController, "World.playerController is missing.");
        int templateIndex = GatInterop.RequirePlayerTemplateIndex();
        Faction faction = GatInterop.RequirePlayerFaction();
        Vector3 position = host.CachedInstancePosition + SpawnOffsetFromHost;
        gat.CreateInstance(Matrix4x4.TRS(position, Quaternion.identity, Vector3.one), templateIndex, handle =>
        {
            if (handle.isNull || World.gat == null || !World.gat.IsHandleValid(handle))
            {
                onFailed("GAT did not create the remote player's avatar.");
                return;
            }
            World.gat.SetInstanceInfo(handle, GatInterop.PlayerControlledInfo(faction));
            onSpawned(handle);
        });
    }

    public void ApplyPose(Handle avatar, ClientPoseMessage pose)
    {
        if (!pose.Position.IsFinite || !pose.Velocity.IsFinite || !float.IsFinite(pose.YawDegrees))
        {
            throw new ArgumentException("Client pose contains non-finite values.", nameof(pose));
        }
        GatSimulation? gat = World.gat;
        if (gat == null || !gat.IsHandleValid(avatar))
        {
            return;
        }
        Matrix4x4 matrix = Matrix4x4.TRS(GatInterop.ToUnity(pose.Position), Quaternion.Euler(0f, pose.YawDegrees, 0f), Vector3.one);
        gat.SetInstanceMatrix(avatar, matrix, movementEnabled: false, collisionEnabled: true, rigidPushEnabled: true);
        gat.SetInstanceVelocity(avatar, GatInterop.ToUnity(pose.Velocity));
    }

    public void Despawn(Handle avatar)
    {
        GatSimulation? gat = World.gat;
        if (gat != null && gat.IsHandleValid(avatar))
        {
            gat.DeleteInstance(avatar);
        }
    }
}
