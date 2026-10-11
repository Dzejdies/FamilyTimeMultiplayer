using System;
using System.Reflection;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Holds;

public static class HandActions
{
    private static readonly MethodInfo RestoreAfterDrop =
        AccessTools.Method(typeof(HandService), "RestoreAfterDrop", new[] { typeof(RigidTransform), typeof(bool) })
        ?? throw new MissingMethodException(nameof(HandService), "RestoreAfterDrop(RigidTransform, bool)");

    private static readonly MethodInfo GrabRigid =
        AccessTools.Method(typeof(HandService), "GrabRigid")
        ?? throw new MissingMethodException(nameof(HandService), "GrabRigid");

    private static readonly MethodInfo RestoreAgentAfterDrop =
        AccessTools.Method(typeof(HandService), "RestoreAfterDrop", new[] { typeof(Handle), typeof(bool) })
        ?? throw new MissingMethodException(nameof(HandService), "RestoreAfterDrop(Handle, bool)");

    private static readonly MethodInfo SetHandHandle =
        AccessTools.Method(typeof(HandService), "SetHandHandle")
        ?? throw new MissingMethodException(nameof(HandService), "SetHandHandle");

    private static readonly MethodInfo GrabHandle =
        AccessTools.Method(typeof(HandService), "GrabHandle")
        ?? throw new MissingMethodException(nameof(HandService), "GrabHandle");

    private static readonly MethodInfo ApplyAgentGrabState =
        AccessTools.Method(typeof(HandService), "ApplyGrabState", new[] { typeof(Handle) })
        ?? throw new MissingMethodException(nameof(HandService), "ApplyGrabState(Handle)");

    public static void GrabAgent(Handle handle) => GrabHandle.Invoke(RequireHand(), new object[] { handle });

    public static void DropAgent(Handle handle)
    {
        HandService hand = RequireHand();
        if (hand.IsHandCarryingGAT(handle))
        {
            SetHandHandle.Invoke(hand, new object[] { Handle.Null });
        }
        RestoreAgentAfterDrop.Invoke(hand, new object[] { handle, false });
    }

    public static void ApplyHostGrabState(Handle handle) => ApplyAgentGrabState.Invoke(RequireHand(), new object[] { handle });

    public static HandService RequireHand() =>
        Game.GatInterop.RequireAlive(PlayerController.hand, "PlayerController.hand is missing.");

    public static void Grab(RigidTransform rigid) => GrabRigid.Invoke(RequireHand(), new object?[] { rigid, null });

    public static void Drop(RigidTransform rigid)
    {
        HandService hand = RequireHand();
        hand.ReleaseCarriedRigid(rigid);
        RestoreAfterDrop.Invoke(hand, new object[] { rigid, false });
    }

    public static bool IsHolding(RigidTransform rigid) => RequireHand().CollectAllCarriedRigids().Contains(rigid);
}
