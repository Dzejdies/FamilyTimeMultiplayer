using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using FamilyTimeMultiplayer.RuntimeHarness.Contracts;
using FamilyTimeMultiplayer.Core.Actions;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Sessions;
using FamilyTimeMultiplayer.Families;
using UnityEngine;

namespace FamilyTimeMultiplayer.RuntimeHarness;

internal sealed class NativeInputProbe
{
    private const int TestSeed = 1234;
    private const float FamilyHudSyncDelaySeconds = 3f;
    private const float HoverWaitSeconds = 10f;
    private const float AnimalSearchDistance = 15f;
    private const float AnimalSpawnDistance = 6f;
    private readonly Func<string, string, object?> _staticValue;
    private readonly Func<object?, string, object?> _value;
    private readonly Action<string, string> _fact;
    private readonly Action<string> _log;
    private bool _bootstrapped;
    private bool _familyPrepared;
    private bool _followTested;
    private int _step;
    private float _stepAt;
    private string? _blockedInputReason;
    private object? _target;
    private Vector3 _targetPosition;

    internal NativeInputProbe(Func<string, string, object?> staticValue, Func<object?, string, object?> value,
        Action<string, string> fact, Action<string> log)
    {
        _staticValue = staticValue;
        _value = value;
        _fact = fact;
        _log = log;
    }

    internal void PrepareFixture()
    {
        if (FamilyTimeMultiplayer.MultiplayerController.Current?.Session is not HostSession host) return;
        foreach (var request in host.PendingFamilyRestores)
        {
            host.DecideFamilyRestore(request.PlayerId, HostSession.FamilyRestoreChoice.Restore);
            _log("Opt-in fixture restored the returning test player's family.");
        }
    }

    internal ScenarioVerdict? Tick(bool worldReady)
    {
        if (!worldReady) { BootstrapNativeWorld(); return null; }
        object? controller = _staticValue("FamilyTimeMultiplayer.MultiplayerController", "Current");
        object? session = _value(controller, "Session");
        if (session == null || (_value(session, "Roster") as IEnumerable)?.Cast<object>().Skip(1).Any() is not true) return null;
        if (_value(session, "Role")?.ToString() == "Host") return SpawnHostAnimal(session);
        if (_value(session, "IsLive") is not true) return null;
        return CheckClientLifecycle();
    }

    private void BootstrapNativeWorld()
    {
        object? gameSession = _staticValue("family.GameSession", "Instance");
        if (_bootstrapped || gameSession == null || _value(gameSession, "State")?.ToString() is not ("Title" or "Menu")) return;
        MethodInfo start = Method(gameSession, "StartNewGame", 3);
        object mode = Enum.GetValues(start.GetParameters()[1].ParameterType).GetValue(0)!;
        _bootstrapped = true;
        start.Invoke(gameSession, new[] { (object)TestSeed, mode, false });
        _log("Opt-in probe started a native test world.");
    }

    private ScenarioVerdict? SpawnHostAnimal(object session)
    {
        if (_step != 0) return ScenarioVerdict.Pass;
        if ((_value(session, "RemotePlayers") as IEnumerable)?.Cast<object>().Any() is not true) return null;
        if (!PrepareDifferentFamilyCounts()) return null;
        if (!CheckHostHud()) return ScenarioVerdict.Fail;
        var isolation = new NativeFamilyIsolationProbe(_staticValue, _value, _fact).Verify();
        if (isolation != ScenarioVerdict.Pass) return isolation;
        object gat = _staticValue("GAT.World", "gat")!;
        object player = _staticValue("GAT.World", "playerController")!;
        Vector3 position = (Vector3)_value(player, "CachedInstancePosition")! + Vector3.forward * AnimalSpawnDistance;
        int templateIndex = FindAnimalTemplate(gat);
        if (templateIndex < 0) return ScenarioVerdict.BlockedPrecondition;
        MethodInfo create = gat.GetType().GetMethods().Single(method => method.Name == "CreateInstances" &&
            method.GetParameters().Length == 3 && method.GetParameters()[1].ParameterType == typeof(int));
        create.Invoke(gat, new object?[] { new[] { Matrix4x4.TRS(position, Quaternion.identity, Vector3.one) }, templateIndex, null });
        _fact("native.hostSpawn", "true");
        _step++;
        return ScenarioVerdict.Pass;
    }

    private bool PrepareDifferentFamilyCounts()
    {
        if (_familyPrepared) return Time.realtimeSinceStartup - _stepAt > FamilyHudSyncDelaySeconds;
        var families = HostFamilyService.Current!;
        object player = _staticValue("GAT.World", "playerController")!;
        families.GrowFamily(families.HostOwner, (Vector3)_value(player, "CachedInstancePosition")!, 1);
        object hunger = _staticValue("GAT.World", "gatHungerBehavior")!;
        Method(hunger, "IncreaseHunger", 0).Invoke(hunger, null);
        _familyPrepared = true;
        _stepAt = Time.realtimeSinceStartup;
        return false;
    }

    private bool CheckHostHud()
    {
        var families = HostFamilyService.Current!;
        FamilyHud.Refresh();
        var displayed = ReadNativeHudFields();
        var expected = families.CountsFor(families.HostOwner);
        _fact("family.hud.host.children", displayed.Children.ToString());
        _fact("family.hud.host.hungry", displayed.Hungry.ToString());
        foreach (var family in families.ToRecord().Families)
        {
            var counts = families.CountsFor(family.Owner);
            _fact("family.hud.expected." + (family.IsHostFamily ? "host" : "client") + ".children", counts.Children.ToString());
            _fact("family.hud.expected." + (family.IsHostFamily ? "host" : "client") + ".hungry", counts.Hungry.ToString());
        }
        return displayed.Equals(expected) && expected.Hungry > 0;
    }

    private FamilyHudCounts ReadNativeHudFields()
    {
        object display = _staticValue("GAT.World", "stateDisplayUI")!;
        return new FamilyHudCounts((int)_value(display, "_totalPlayerCount")!,
            (int)_value(display, "_hungryTotal")!, (int)_value(display, "_agentsNotSleepingCount")!);
    }

    private bool CheckClientHud()
    {
        if (!FamilyHud.Override.HasValue) return false;
        FamilyHud.Refresh();
        var displayed = ReadNativeHudFields();
        int members = LocalFamilyView.PhysicalMembers().Length;
        _fact("family.hud.client.children", displayed.Children.ToString());
        _fact("family.hud.client.hungry", displayed.Hungry.ToString());
        _fact("family.hud.client.members", members.ToString());
        return members > 0 && displayed.Children == members && displayed.Hungry > 0;
    }

    private int FindAnimalTemplate(object gat)
    {
        int index = 0;
        foreach (object entry in (IEnumerable)Method(gat, "GetTemplateEntries", 0).Invoke(gat, null)!)
        {
            object? template = _value(entry, "template");
            if (template != null && _value(template, "faction")?.ToString() == "PASSIVE") return index;
            index++;
        }
        return -1;
    }

    private ScenarioVerdict? CheckClientLifecycle()
    {
        object service = _staticValue("GAT.World", "commandService")!;
        if (_step == 0) return BeginClientChecks(service);
        if (_step == 1) return ObserveHover(service);
        Aim(Vector3.up);
        if (Time.realtimeSinceStartup - _stepAt < HoverWaitSeconds) return null;
        bool cleared = _value(service, "_lastShowInteract") is false;
        object? hand = _staticValue("GAT.PlayerController", "hand");
        bool hoverCleared = _value(hand, "_hasCurrentDistantHoverCandidate") is false;
        _fact("native.hoverCleared", hoverCleared.ToString());
        _fact("native.interactIndicatorCleared", cleared.ToString());
        _fact("native.queryPending", _value(hand, "_interactionQueryPending")?.ToString() ?? "unknown");
        return cleared && hoverCleared ? VerifyDisconnect(service) : ScenarioVerdict.Fail;
    }

    private ScenarioVerdict? BeginClientChecks(object service)
    {
        if (!CheckClientHud()) return null;
        object? menu = _staticValue("GAT.PauseMenuBook", "ActiveInstance");
        if (menu != null) Method(menu, "Close", 0).Invoke(menu, null);
        if (!_followTested)
        {
            InvokeInputCallback(service, "OnFollowStarted");
            if (_value(service, "_followInputAccepted") is not true)
            {
                ReportBlockedInput();
                return null;
            }
            InvokeInputCallback(service, "OnFollowCanceled");
            bool released = _value(service, "_followInputAccepted") is false && _value(_value(service, "_follow"), "_followPressed") is false;
            _fact("native.followReleased", released.ToString());
            if (!released) return ScenarioVerdict.Fail;
            _followTested = true;
        }
        if (!FindNearbyAnimal()) return null;
        _step = 1;
        _stepAt = Time.realtimeSinceStartup;
        return null;
    }

    private void ReportBlockedInput()
    {
        string reason = $"disableControls={_value(_staticValue("GAT.PlayerController", "disableAllControls"), "value")}; " +
            $"cursor={_value(_staticValue("GAT.PlayerController", "showCursor"), "value")}; " +
            $"gameState={_value(_staticValue("family.GameSession", "Instance"), "State")}; " +
            $"tutorial={_value(_staticValue("GAT.World", "tutorialManager"), "RequiredActive")}; " +
            $"lockUI={_staticValue("GAT.LockUI", "IsOpen")}";
        _fact("native.inputBlocked", reason);
        if (reason != _blockedInputReason) _log(reason);
        _blockedInputReason = reason;
    }

    private ScenarioVerdict? ObserveHover(object service)
    {
        FindNearbyAnimal();
        if (_target != null) Aim(_targetPosition - QueryCamera.transform.position);
        object? hand = _staticValue("GAT.PlayerController", "hand");
        if ((_target == null || _value(hand, "_hasCurrentDistantHoverCandidate") is not true) &&
            Time.realtimeSinceStartup - _stepAt < HoverWaitSeconds) return null;
        _fact("native.hoverQueryPending", _value(hand, "_interactionQueryPending")?.ToString() ?? "unknown");
        _fact("native.hoverGrabLocked", _value(hand, "_grabLocked")?.ToString() ?? "unknown");
        _fact("native.hoverCarrying", _value(hand, "IsHandCarrying")?.ToString() ?? "unknown");
        _fact("native.queryCameraIsMain", (QueryCamera == Camera.main).ToString());
        _fact("native.targetPoint", _targetPosition.ToString());
        _fact("native.targetDistance", Vector3.Distance(QueryCamera.transform.position, _targetPosition).ToString());
        if (_target == null || _value(hand, "_hasCurrentDistantHoverCandidate") is not true) return ScenarioVerdict.BlockedPrecondition;
        _fact("native.hoverAcquired", "true");
        Method(service, "BeginContinuousAttackSelection", 2).Invoke(service, new[] { _target!, (object)_targetPosition });
        bool started = _value(service, "_continuousSelectionCrosshair") != null;
        Method(service, "OnLongPressInteract", 1).Invoke(service, new object[] { false });
        bool released = _value(service, "_continuousSelectionCrosshair") == null && _value(_value(service, "_attack"), "ContinuousSelectionActive") is false;
        _fact("native.selectionStarted", started.ToString());
        _fact("native.selectionReleased", released.ToString());
        if (!started || !released) return ScenarioVerdict.Fail;
        _step = 2;
        _stepAt = Time.realtimeSinceStartup;
        return null;
    }

    private ScenarioVerdict VerifyDisconnect(object service)
    {
        var client = (ClientSession)FamilyTimeMultiplayer.MultiplayerController.Current!.Session!;
        bool retryRejected = false;
        Action<GenericActionResultMessage> completed = result =>
        {
            if (result.Outcome != GenericActionOutcome.Unknown) return;
            try { QueueFollowProbe(client); }
            catch (InvalidOperationException) { retryRejected = true; }
        };
        client.GenericActionCompleted += completed;
        try { return DisconnectWithGesture(client, service, () => retryRejected); }
        finally { client.GenericActionCompleted -= completed; }
    }

    private ScenarioVerdict DisconnectWithGesture(ClientSession client, object service, Func<bool> retryRejected)
    {
        InvokeInputCallback(service, "OnFollowStarted");
        Method(service, "BeginContinuousAttackSelection", 2).Invoke(service, new[] { _target!, (object)_targetPosition });
        QueueFollowProbe(client);
        client.Dispose();
        bool crosshairCleared = _value(service, "_continuousSelectionCrosshair") == null;
        bool followReleased = _value(service, "_followInputAccepted") is false;
        bool cleaned = crosshairCleared && followReleased;
        _fact("native.disconnectCrosshairCleared", crosshairCleared.ToString());
        _fact("native.disconnectFollowReleased", followReleased.ToString());
        _fact("native.disconnectCleaned", cleaned.ToString());
        _fact("actions.retryDuringDisconnectRejected", retryRejected().ToString());
        return cleaned && retryRejected() ? ScenarioVerdict.Pass : ScenarioVerdict.Fail;
    }

    private static Guid QueueFollowProbe(ClientSession client) =>
        client.RequestGenericAction(NativeCommandActionAdapter.ActionId, NativeCommandActionAdapter.SchemaVersion, null, 0,
            MessageCodec.Encode(CommandIntentMessage.Follow(true)));

    private static void InvokeInputCallback(object service, string name)
    {
        MethodInfo callback = Method(service, name, 1);
        callback.Invoke(service, new[] { Activator.CreateInstance(callback.GetParameters()[0].ParameterType) });
    }

    private bool FindNearbyAnimal()
    {
        object gat = _staticValue("GAT.World", "gat")!;
        _target = null;
        float nearest = AnimalSearchDistance;
        foreach (object entry in (IEnumerable)Method(gat, "GetTemplateEntries", 0).Invoke(gat, null)!)
        {
            if (_value(_value(entry, "template"), "faction")?.ToString() != "PASSIVE") continue;
            int start = Convert.ToInt32(_value(entry, "startIndex"));
            int count = Convert.ToInt32(_value(entry, "count"));
            for (int index = start; index < start + count; index++) SelectNearerAnimal(gat, (ushort)index, ref nearest);
        }
        return _target != null;
    }

    private void SelectNearerAnimal(object gat, ushort physical, ref float nearest)
    {
        object handle = Method(gat, "GetHandle", 1).Invoke(gat, new object[] { physical })!;
        object? matrix = Method(gat, "GetInstanceMatrixImmediate", 1).Invoke(gat, new[] { handle });
        if (matrix is not Matrix4x4 transform) return;
        Vector3 position = transform.GetColumn(3);
        float distance = Vector3.Distance(position, QueryCamera.transform.position);
        if (distance >= nearest) return;
        nearest = distance;
        _target = handle;
        _targetPosition = TargetCenter(gat, handle, transform);
    }

    private Vector3 TargetCenter(object gat, object handle, Matrix4x4 transform)
    {
        object template = Method(gat, "GetTemplateFromHandle", 1).Invoke(gat, new[] { handle })!;
        object? mesh = Method(template, "GetPrimaryMesh", 0).Invoke(template, null);
        return _value(mesh, "bounds") is Bounds bounds ? transform.MultiplyPoint3x4(bounds.center) : (Vector3)transform.GetColumn(3);
    }

    private Camera QueryCamera => (Camera?)_value(_staticValue("GAT.PlayerController", "hand"), "Camera") ?? Camera.main;

    private void Aim(Vector3 direction)
    {
        Quaternion rotation = Quaternion.LookRotation(direction.normalized);
        object player = _staticValue("GAT.World", "playerController")!;
        player.GetType().GetField("yaw", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(player, rotation.eulerAngles.y);
        float pitch = rotation.eulerAngles.x > 180f ? rotation.eulerAngles.x - 360f : rotation.eulerAngles.x;
        player.GetType().GetField("pitch", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(player, pitch);
        QueryCamera.transform.rotation = rotation;
    }

    private static MethodInfo Method(object target, string name, int parameterCount) =>
        target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(method => method.Name == name && method.GetParameters().Length == parameterCount);
}
