using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using FamilyTimeMultiplayer.RuntimeHarness.Contracts;
using UnityEngine;

namespace FamilyTimeMultiplayer.RuntimeHarness;

internal sealed class NativeInputProbe
{
    private const int TestSeed = 1234;
    private const float HoverWaitSeconds = 3f;
    private readonly Func<string, string, object?> _staticValue;
    private readonly Func<object?, string, object?> _value;
    private readonly Action<string, string> _fact;
    private readonly Action<string> _log;
    private bool _bootstrapped;
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
        if (FamilyTimeMultiplayer.MultiplayerController.Current?.Session is not FamilyTimeMultiplayer.Sessions.HostSession host) return;
        foreach (var request in host.PendingFamilyRestores)
        {
            host.DecideFamilyRestore(request.PlayerId, FamilyTimeMultiplayer.Sessions.HostSession.FamilyRestoreChoice.Restore);
            _log("Opt-in fixture restored the returning test player's family.");
        }
    }

    internal ScenarioVerdict? Tick(bool worldReady)
    {
        if (!worldReady) { BootstrapNativeWorld(); return null; }
        object? controller = _staticValue("FamilyTimeMultiplayer.MultiplayerController", "Current");
        object? session = _value(controller, "Session");
        if (session == null || Convert.ToInt32(_value(_value(session, "Roster"), "Count") ?? 0) < 2) return null;
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
        object gat = _staticValue("GAT.World", "gat")!;
        object player = _staticValue("GAT.World", "playerController")!;
        Vector3 position = (Vector3)_value(player, "CachedInstancePosition")! + Vector3.forward * 6f;
        int templateIndex = FindAnimalTemplate(gat);
        if (templateIndex < 0) return ScenarioVerdict.BlockedPrecondition;
        MethodInfo create = gat.GetType().GetMethods().Single(method => method.Name == "CreateInstances" &&
            method.GetParameters().Length == 3 && method.GetParameters()[1].ParameterType == typeof(int));
        create.Invoke(gat, new object?[] { new[] { Matrix4x4.TRS(position, Quaternion.identity, Vector3.one) }, templateIndex, null });
        _fact("native.hostSpawn", "true");
        _step++;
        return ScenarioVerdict.Pass;
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
        return cleared && hoverCleared ? ScenarioVerdict.Pass : ScenarioVerdict.Fail;
    }

    private ScenarioVerdict? BeginClientChecks(object service)
    {
        object? menu = _staticValue("GAT.PauseMenuBook", "ActiveInstance");
        if (menu != null) Method(menu, "Close", 0).Invoke(menu, null);
        MethodInfo start = Method(service, "OnFollowStarted", 1);
        start.Invoke(service, new[] { Activator.CreateInstance(start.GetParameters()[0].ParameterType) });
        if (_value(service, "_followInputAccepted") is not true)
        {
            string reason = $"disableControls={_value(_staticValue("GAT.PlayerController", "disableAllControls"), "value")}; cursor={_value(_staticValue("GAT.PlayerController", "showCursor"), "value")}; gameState={_value(_staticValue("family.GameSession", "Instance"), "State")}; tutorial={_value(_staticValue("GAT.World", "tutorialManager"), "RequiredActive")}; lockUI={_staticValue("GAT.LockUI", "IsOpen")}";
            _fact("native.inputBlocked", reason);
            if (reason != _blockedInputReason) _log(reason);
            _blockedInputReason = reason;
            return null;
        }
        MethodInfo cancel = Method(service, "OnFollowCanceled", 1);
        cancel.Invoke(service, new[] { Activator.CreateInstance(cancel.GetParameters()[0].ParameterType) });
        bool released = _value(service, "_followInputAccepted") is false && _value(_value(service, "_follow"), "_followPressed") is false;
        _fact("native.followReleased", released.ToString());
        if (!released) return ScenarioVerdict.Fail;
        if (!FindNearbyAnimal()) return null;
        _step = 1;
        _stepAt = Time.realtimeSinceStartup;
        return null;
    }

    private ScenarioVerdict? ObserveHover(object service)
    {
        Aim(_targetPosition - Camera.main.transform.position);
        if (Time.realtimeSinceStartup - _stepAt < HoverWaitSeconds) return null;
        object? hand = _staticValue("GAT.PlayerController", "hand");
        if (_value(hand, "_hasCurrentDistantHoverCandidate") is not true) return ScenarioVerdict.BlockedPrecondition;
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

    private bool FindNearbyAnimal()
    {
        object gat = _staticValue("GAT.World", "gat")!;
        float nearest = 25f;
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
        float distance = Vector3.Distance(position, Camera.main.transform.position);
        if (distance >= nearest) return;
        nearest = distance;
        _target = handle;
        _targetPosition = position + Vector3.up * 0.6f;
    }

    private void Aim(Vector3 direction)
    {
        Quaternion rotation = Quaternion.LookRotation(direction.normalized);
        object player = _staticValue("GAT.World", "playerController")!;
        player.GetType().GetField("yaw", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(player, rotation.eulerAngles.y);
        float pitch = rotation.eulerAngles.x > 180f ? rotation.eulerAngles.x - 360f : rotation.eulerAngles.x;
        player.GetType().GetField("pitch", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(player, pitch);
        Camera.main.transform.rotation = rotation;
    }

    private static MethodInfo Method(object target, string name, int parameterCount) =>
        target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(method => method.Name == name && method.GetParameters().Length == parameterCount);
}
