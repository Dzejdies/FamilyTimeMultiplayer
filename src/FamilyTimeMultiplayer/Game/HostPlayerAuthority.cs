using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Safety;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Game;

public static class HostPlayerAuthority
{
    private const float DefaultHandRaycastDistance = 50f;
    private sealed class Player
    {
        public Handle Avatar;
        public MotionBudget Motion = null!;
        public ClientPoseMessage Pose = null!;
        public bool AddonPose;
        public ClientPoseMessage? AddonLook;
    }
    private static readonly Dictionary<int, Player> Players = new Dictionary<int, Player>();
    public static float HandReach => PlayerController.hand != null ? Traverse.Create(PlayerController.hand).Field<float>("farMaxDistance").Value + 3 : DefaultHandRaycastDistance + 3;
    public static float HandRaycastDistance => PlayerController.hand != null ? Traverse.Create(PlayerController.hand).Field<float>("farMaxDistance").Value : DefaultHandRaycastDistance;
    public static float Speed => Math.Max(30, Traverse.Create(World.playerController).Field<float>("runSpeed").Value * 1.5f);

    public static void Register(int id, Handle avatar)
    {
        Vector3 origin = World.gat.GetInstanceMatrixImmediate(avatar)?.GetColumn(3) ?? LocalPlayer.Position + Vector3.right * 2;
        var point = GatInterop.ToNet(origin);
        Players[id] = new Player { Avatar = avatar, Motion = new MotionBudget(point, FamilyTimeMultiplayer.Game.GameClock.Now, Speed, 5), Pose = new ClientPoseMessage(point, 0, 0, NetVector3.Zero) };
    }
    public static void Remove(int id) => Players.Remove(id);
    public static void Clear() => Players.Clear();
    public static bool Exists(int id) => Players.ContainsKey(id);
    public static bool IsAvatar(Handle handle)
    {
        foreach (Player player in Players.Values) if (player.Avatar == handle) return true;
        return handle == LocalPlayer.PossessedHandle;
    }
    public static ClientPoseMessage RequirePose(int id) => Players.TryGetValue(id, out Player player) ? player.Pose : throw new InvalidDataException("Player has no host-authorized avatar.");

    public static ClientPoseMessage RequireCommandPose(int id) => AddonCommandPose.Compose(RequirePose(id), Players[id].AddonLook);

    public static void AcceptAddonLook(int id, ClientPoseMessage authenticatedLook)
    {
        if (!Players.TryGetValue(id, out Player player) || !player.AddonPose) throw new InvalidDataException("Player has no host-authorized addon pose.");
        AddonCommandPose.Compose(player.Pose, authenticatedLook);
        player.AddonLook = authenticatedLook;
    }

    public static bool TryGetAddonPose(ushort avatar, out ClientPoseMessage pose)
    {
        foreach (Player player in Players.Values)
        {
            if (player.AddonPose && player.Avatar.stableIndex == avatar) { pose = player.Pose; return true; }
        }
        pose = null!; return false;
    }
    public static void AcceptAuthoritativePose(int id, ClientPoseMessage pose)
    {
        if (!MotionBudget.ValidPose(pose)) throw new InvalidOperationException("Invalid host-authored player pose.");
        if (Players.TryGetValue(id, out Player player)) player.Pose = pose;
    }
    public static void AcceptAddonPose(int id, ClientPoseMessage pose)
    {
        AcceptAuthoritativePose(id, pose);
        if (Players.TryGetValue(id, out Player player)) player.AddonPose = true;
    }
    public static void ReleaseAddonPose(int id)
    {
        if (!Players.TryGetValue(id, out Player player) || !player.AddonPose) return;
        player.Motion = new MotionBudget(player.Pose.Position, GameClock.Now, Speed, 5);
        player.AddonPose = false;
        player.AddonLook = null;
    }

    public static void AcceptPose(int id, ClientPoseMessage pose)
    {
        if (!Players.TryGetValue(id, out Player player) || !MotionBudget.ValidPose(pose) ||
            new Vector2(pose.Velocity.X, pose.Velocity.Z).magnitude > Speed || !player.Motion.TryMove(pose.Position, FamilyTimeMultiplayer.Game.GameClock.Now))
            throw new InvalidDataException($"Client movement exceeds the authorized avatar motion budget: position={pose.Position}, velocity={pose.Velocity}.");
        player.Pose = pose;
    }
    public static void RequireNear(int id, Vector3 point, float reach)
    {
        if (!MotionBudget.Near(RequirePose(id).Position, GatInterop.ToNet(point), reach)) throw new InvalidDataException("Target is outside the player's authorized reach.");
    }
    public static void RequireRigidNear(int id, RigidTransform rigid, float reach)
    {
        if (!MotionBudget.InWorld(GatInterop.ToNet(rigid.position))) throw new InvalidDataException("Rigid target is outside the world.");
        Vector3 eye = GatInterop.ToUnity(RequirePose(id).Position) + Vector3.up * 1.7f;
        Vector3 scale = RigidTransformApi.Scale(rigid);
        Vector3 center = Vector3.Scale((Vector3)RigidTransformApi.DefinitionValue(rigid, "collisionCenter1")!, scale);
        Vector3 local = Quaternion.Inverse(rigid.rotation) * (eye - rigid.position);
        Vector3 closest;
        if ((bool)RigidTransformApi.DefinitionValue(rigid, "capsuleCollider")!)
        {
            Vector3 end = Vector3.Scale((Vector3)RigidTransformApi.DefinitionValue(rigid, "collisionCenter2")!, scale);
            Vector3 delta = end - center;
            float t = delta.sqrMagnitude > 0 ? Mathf.Clamp01(Vector3.Dot(local - center, delta) / delta.sqrMagnitude) : 0;
            Vector3 axis = center + delta * t;
            float radius = (float)RigidTransformApi.DefinitionValue(rigid, "collisionRadius")! * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            closest = axis + ClampMagnitude(local - axis, radius);
        }
        else
        {
            Vector3 half = Vector3.Scale((Vector3)RigidTransformApi.DefinitionValue(rigid, "collisionSize")!, scale) * 0.5f;
            Vector3 delta = local - center;
            closest = center + new Vector3(Mathf.Clamp(delta.x, -Mathf.Abs(half.x), Mathf.Abs(half.x)), Mathf.Clamp(delta.y, -Mathf.Abs(half.y), Mathf.Abs(half.y)), Mathf.Clamp(delta.z, -Mathf.Abs(half.z), Mathf.Abs(half.z)));
        }
        if (Vector3.Distance(local, closest) > reach) throw new InvalidDataException("Rigid target is outside the player's authorized surface reach.");
    }

    private static Vector3 ClampMagnitude(Vector3 value, float maximum)
    {
        float magnitude = value.magnitude;
        return magnitude > maximum && magnitude > 0 ? value * (maximum / magnitude) : value;
    }

    public static Vector3 AgentPosition(ushort id)
    {
        Handle handle = GatInterop.ToHandle(id);
        if (World.gat == null || !World.gat.IsHandleValid(handle)) throw new InvalidDataException("Unknown agent target.");
        return World.gat.GetInstanceMatrixImmediate(handle)?.GetColumn(3) ?? throw new InvalidDataException("Agent target position is unavailable.");
    }
    public static void RequireOwnedAgent(int id, ushort agent)
    {
        Families.HostFamilyService? families = Families.HostFamilyService.Current;
        if (families == null) return;
        if (families.MembersOf(families.OwnerOfPlayer(id)).Contains(agent)) return;
        if (families.Mamas.TryGetMamaOf(id, out WolfMama mama) && mama.FakeGATHandle.stableIndex == agent) return;
        if (World.gat.GetTemplateFromHandle(GatInterop.ToHandle(agent))?.faction != Faction.PLAYER) return;
        throw new InvalidDataException("The agent belongs to a different player.");
    }
}
