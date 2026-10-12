using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Families;
using FamilyTimeMultiplayer.RuntimeHarness.Contracts;
using UnityEngine;
using static FamilyTimeMultiplayer.RuntimeHarness.NativeProbeReflection;

namespace FamilyTimeMultiplayer.RuntimeHarness;

internal sealed class NativeFamilyIsolationProbe
{
    private readonly Func<object?, string, object?> _value;
    private readonly Func<string, string, object?> _staticValue;
    private readonly Action<string, string> _fact;

    internal NativeFamilyIsolationProbe(Func<string, string, object?> staticValue, Func<object?, string, object?> value, Action<string, string> fact)
    {
        _staticValue = staticValue;
        _value = value;
        _fact = fact;
    }

    internal ScenarioVerdict Verify()
    {
        var hub = HostCommandHub.Current;
        var families = HostFamilyService.Current;
        if (hub == null || families == null) return ScenarioVerdict.BlockedPrecondition;
        object[] controllers = ((IEnumerable)Invoke(hub, "FollowControllers")!).Cast<object>().ToArray();
        if (controllers.Length < 2) return ScenarioVerdict.BlockedPrecondition;
        return CheckOrder(hub, families, controllers, "hostFirst") &&
            CheckOrder(hub, families, controllers.Reverse().ToArray(), "clientFirst")
            ? ScenarioVerdict.Pass : ScenarioVerdict.Fail;
    }

    private bool CheckOrder(HostCommandHub hub, HostFamilyService families, object[] controllers, string order)
    {
        foreach (object entry in controllers) InContext(hub, entry, follow => Invoke(follow, "StopFollowing", false));
        foreach (object entry in controllers) InContext(hub, entry, follow => Invoke(follow, "StartFollowing"));
        bool isolated = controllers.All(entry => CheckFollowers(families, entry, order) && CheckDeferredTarget(hub, entry, order));
        object host = controllers.Single(entry => (int)_value(entry, "Item1")! == PlayerRosterMessage.HostPlayerId);
        object remote = controllers.First(entry => (int)_value(entry, "Item1")! != PlayerRosterMessage.HostPlayerId);
        bool hostTargetRestored = false;
        InContext(hub, remote, _ => hostTargetRestored = CheckDeferredTarget(hub, host, order + ".nestedHost"));
        isolated &= hostTargetRestored;
        foreach (object entry in controllers) InContext(hub, entry, follow => Invoke(follow, "StopFollowing", false));
        _fact("family.follow." + order + ".isolated", isolated.ToString());
        return isolated;
    }

    private bool CheckFollowers(HostFamilyService families, object entry, string order)
    {
        int playerId = (int)_value(entry, "Item1")!;
        object follow = _value(entry, "Item2")!;
        object? request = _value(follow, "FollowFlowFieldRequest");
        if (request == null) return false;
        var owned = new HashSet<ushort>(families.MembersOf(families.OwnerOfPlayer(playerId)));
        var allChildren = new HashSet<ushort>(families.ToRecord().Families.SelectMany(family => family.Members));
        ushort[] children = ((IEnumerable)Invoke(request, "GetAssignedAgentSnapshot")!).Cast<object>()
            .Select(handle => (ushort)_value(handle, "stableIndex")!).Where(allChildren.Contains).ToArray();
        _fact($"family.follow.{order}.{playerId}.owned", string.Join(",", owned));
        _fact($"family.follow.{order}.{playerId}.assigned", string.Join(",", children));
        return owned.Count > 0 && children.Length > 0 && children.All(owned.Contains);
    }

    private bool CheckDeferredTarget(HostCommandHub hub, object entry, string order)
    {
        Vector3Int expected = default;
        InContext(hub, entry, _ =>
        {
            object player = _staticValue("GAT.World", "playerController")!;
            object gat = _staticValue("GAT.World", "gat")!;
            object handle = _value(player, "PossessedHandle")!;
            var matrix = (Matrix4x4)Invoke(gat, "GetInstanceMatrixImmediate", handle)!;
            expected = Vector3Int.FloorToInt(matrix.GetColumn(3));
        });
        object follow = _value(entry, "Item2")!;
        Invoke(follow, "RequestFollowFlowField", (int)_value(follow, "FollowAgentCount")!);
        object? request = _value(follow, "FollowFlowFieldRequest");
        var actual = (Vector3Int)_value(request, "_requestedEnd")!;
        int playerId = (int)_value(entry, "Item1")!;
        _fact($"family.follow.{order}.{playerId}.target", actual.ToString());
        _fact($"family.follow.{order}.{playerId}.expectedTarget", expected.ToString());
        return actual == expected;
    }

    private void InContext(HostCommandHub hub, object entry, Action<object> operation)
    {
        int playerId = (int)_value(entry, "Item1")!;
        object follow = _value(entry, "Item2")!;
        if (playerId == PlayerRosterMessage.HostPlayerId)
        {
            using (CommanderContext.EnterNativeHost()) operation(follow);
        }
        else Invoke(hub, "RunAsPlayer", playerId, (Action)(() => operation(follow)));
    }

}
