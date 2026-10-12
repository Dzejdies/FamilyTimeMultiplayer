using System;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Families;
using FamilyTimeMultiplayer.Core.Protocol;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Game.Actions;

internal static class NativeCommandForwarding
{
    internal static bool Owns(object controller, string field) =>
        ClientCommandBridge.CanForwardNativeAction && World.commandService != null &&
        ReferenceEquals(controller, Traverse.Create(World.commandService).Field<object>(field).Value);

    internal static bool SendRigid(RigidTransform rigid)
    {
        if (rigid == null || !ClientCommandBridge.TryGetNetId(rigid, out int identity)) return MissingTarget();
        ClientCommandBridge.Send(CommandIntentMessage.InteractRigid(identity));
        return true;
    }

    internal static bool SendAttack(Handle target, Vector3 position)
    {
        if (target.isNull || !ClientCommandBridge.TryGetHostAgent(target.stableIndex, out ushort identity)) return MissingTarget();
        ClientCommandBridge.Send(CommandIntentMessage.InteractAgent(identity, GatInterop.ToNet(position)));
        return true;
    }

    internal static bool SendCommandLock(Handle target, bool locked)
    {
        if (!LocalFamilyView.IncludesChild(target)) return false;
        if (target.isNull || !ClientCommandBridge.TryGetHostAgent(target.stableIndex, out ushort identity)) return MissingTarget();
        ClientCommandBridge.Send(CommandIntentMessage.SetCommandLock(identity, locked));
        return true;
    }

    internal static bool SendPlan(BuildPlanManager.BuildPlan? plan)
    {
        if (plan == null || World.buildPartManager == null) return false;
        BuildPart part = World.buildPartManager.BuildPartTemplates[plan.templateIndex];
        Vector3 center = part.GetPlacementMatrix(plan.gridPosition, plan.rotation).MultiplyPoint3x4(part.GetCombinedMeshBounds().center);
        ClientCommandBridge.Send(CommandIntentMessage.InteractBuildPlan(GatInterop.ToNet(center)));
        return true;
    }

    private static bool MissingTarget()
    {
        ClientCommandBridge.ShowNotice("This target has not been bound to the shared world yet.");
        return false;
    }
}
