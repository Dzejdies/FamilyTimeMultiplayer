using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FamilyTimeMultiplayer.Core.Actions;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Replication;
using FamilyTimeMultiplayer.Families;
using FamilyTimeMultiplayer.RuntimeHarness.Contracts;
using FamilyTimeMultiplayer.Sessions;
using UnityEngine;
using static FamilyTimeMultiplayer.RuntimeHarness.NativeProbeReflection;

namespace FamilyTimeMultiplayer.RuntimeHarness;

internal sealed class NativeCommandLockProbe
{
    private enum LockPhase { AwaitSnapshot, RepeatedSet, ExplicitUnlock, BatchLock, ReleaseOwnLocks, ForeignRefusal }
    private const float PhaseTimeoutSeconds = 20f;
    private readonly Func<string, string, object?> _staticValue;
    private readonly Func<object?, string, object?> _value;
    private readonly Action<string, string> _fact;
    private readonly HashSet<Guid> _tracked = new();
    private readonly List<GenericActionOutcome> _completed = new();
    private ClientSession? _client;
    private object[] _children = Array.Empty<object>();
    private object? _foreign;
    private bool _seeded;
    private LockPhase _phase;
    private float _phaseAt;
    private int _expected;

    internal NativeCommandLockProbe(Func<string, string, object?> staticValue, Func<object?, string, object?> value,
        Action<string, string> fact)
    {
        _staticValue = staticValue;
        _value = value;
        _fact = fact;
    }

    internal void PrepareHost()
    {
        if (_seeded || HostFamilyService.Current is not { } families) return;
        ushort child = families.MembersOf(families.HostOwner).First();
        object target = Workers().Single(handle => Index(handle) == child);
        if (Invoke(Ownership, "SetCommandLocked", target, true) is not true)
            throw new InvalidOperationException("Native fixture could not lock the host's child.");
        _seeded = true;
        _fact("locks.hostSeed", child.ToString());
    }

    internal bool VerifyHostFamilyGate()
    {
        var families = HostFamilyService.Current!;
        var owned = new HashSet<ushort>(families.MembersOf(families.HostOwner));
        object foreign = Workers().First(handle => !owned.Contains(Index(handle)));
        bool before = Locked(foreign);
        bool refused = Invoke(Ownership, "SetCommandLocked", foreign, !before) is false && Locked(foreign) == before;
        _fact("locks.hostForeignMutationRefused", refused.ToString());
        return refused;
    }

    internal ScenarioVerdict? Tick(ClientSession client)
    {
        if (_phase == LockPhase.AwaitSnapshot) return Begin(client);
        if (Time.realtimeSinceStartup - _phaseAt > PhaseTimeoutSeconds) return Fail("timeout phase=" + _phase);
        if (_completed.Count < _expected) return null;
        bool expectedRejected = _phase == LockPhase.ForeignRefusal;
        if (_completed.Any(outcome => outcome != (expectedRejected ? GenericActionOutcome.Rejected : GenericActionOutcome.Executed)))
            return Fail("unexpected action result phase=" + _phase);
        return Advance();
    }

    private ScenarioVerdict? Begin(ClientSession client)
    {
        ushort[] members = (_staticValue("FamilyTimeMultiplayer.Families.LocalFamilyView", "_members") as ushort[]) ?? Array.Empty<ushort>();
        if (members.Length == 0) return null;
        object[] workers = Workers();
        _children = workers.Where(handle => client.TryGetHostAgent(Index(handle), out ushort host) && members.Contains(host)).ToArray();
        _foreign = workers.FirstOrDefault(handle => !_children.Contains(handle) && Locked(handle));
        if (_children.Length != members.Length || _foreign == null) return null;
        if (!SnapshotOwnsLockState(client, _foreign)) return null;
        _fact("locks.snapshotApplied", "true");
        _fact("locks.structuralWorkerCount", _value(Ownership, "WorkerTemplateAgentCount")!.ToString()!);
        if ((int)_value(Ownership, "WorkerTemplateAgentCount")! != workers.Length) return Fail("structural count mismatch");
        if (Candidate(_foreign) || _children.Any(handle => !Candidate(handle)) || HasOwnLocks()) return Fail("family candidate mismatch");
        _fact("locks.foreignExcludedFromNativeUI", "true");
        _client = client;
        client.GenericActionCompleted += OnCompleted;
        StartPhase(LockPhase.RepeatedSet, () => { Invoke(Ownership, "SetCommandLocked", _children[0], true); Invoke(Ownership, "SetCommandLocked", _children[0], true); });
        return null;
    }

    private ScenarioVerdict? Advance()
    {
        switch (_phase)
        {
            case LockPhase.RepeatedSet:
                if (!Locked(_children[0])) return null;
                _fact("locks.repeatedSetPreserved", "true");
                StartPhase(LockPhase.ExplicitUnlock, () => Invoke(Ownership, "SetCommandLocked", _children[0], false));
                break;
            case LockPhase.ExplicitUnlock:
                if (Locked(_children[0])) return null;
                _fact("locks.explicitUnlockApplied", "true");
                StartPhase(LockPhase.BatchLock, LockOwnBatch);
                break;
            case LockPhase.BatchLock:
                if (_children.Any(handle => !Locked(handle))) return null;
                if (!HasOwnLocks()) return Fail("native UI did not see own locks");
                _fact("locks.batchApplied", "true");
                StartPhase(LockPhase.ReleaseOwnLocks, () => Invoke(Service, "ReleaseAllCommandLocks"));
                break;
            case LockPhase.ReleaseOwnLocks:
                if (_children.Any(Locked)) return null;
                if (HasOwnLocks() || !Locked(_foreign!)) return Fail("release affected a foreign family or left own locks");
                _fact("locks.nativeReleaseOwnOnly", "true");
                StartPhase(LockPhase.ForeignRefusal, RequestForeignUnlock);
                break;
            case LockPhase.ForeignRefusal:
                if (!Locked(_foreign!)) return Fail("foreign lock changed");
                _fact("locks.foreignRequestRejected", "true");
                _client!.GenericActionCompleted -= OnCompleted;
                return ScenarioVerdict.Pass;
        }
        return null;
    }

    private void LockOwnBatch()
    {
        object[] mixed = _children.Concat(new[] { _foreign! }).ToArray();
        Array handles = Array.CreateInstance(_children[0].GetType(), mixed.Length);
        for (int index = 0; index < mixed.Length; index++) handles.SetValue(mixed[index], index);
        int accepted = (int)Invoke(Ownership, "SetCommandLocked", handles, true)!;
        _fact("locks.batchAccepted", accepted.ToString());
        if (accepted != _children.Length) throw new InvalidOperationException("Native batch forwarding included a foreign child.");
    }

    private void RequestForeignUnlock()
    {
        var registry = (GenericEntityRegistry<object>)_value(_client, "_entityRegistry")!;
        _client!.TryGetHostAgent(Index(_foreign!), out ushort hostIndex);
        var identity = new NetworkEntityId(registry.WorldGeneration, new EntityKind("agent"), hostIndex);
        if (!registry.TryGetLifecycleRevision(identity, out ulong revision)) throw new InvalidOperationException("Foreign fixture binding is missing.");
        _tracked.Add(_client.RequestGenericAction(NativeCommandActionAdapter.ActionId, NativeCommandActionAdapter.SchemaVersion, identity, revision,
            MessageCodec.Encode(CommandIntentMessage.SetCommandLock(hostIndex, false))));
    }

    private void StartPhase(LockPhase phase, Action invokeNative)
    {
        _phase = phase;
        _phaseAt = Time.realtimeSinceStartup;
        _tracked.Clear();
        _completed.Clear();
        var pending = (HashSet<Guid>)_value(_client, "_pendingNativeCommands")!;
        var before = new HashSet<Guid>(pending);
        invokeNative();
        foreach (Guid request in pending.Except(before)) _tracked.Add(request);
        _expected = _tracked.Count;
        if (_expected == 0) throw new InvalidOperationException("Native lock fixture sent no request.");
    }

    private bool SnapshotOwnsLockState(ClientSession client, object handle)
    {
        var registry = (GenericEntityRegistry<object>)_value(client, "_entityRegistry")!;
        return registry.TryGetIdentity(Index(handle), out NetworkEntityId identity) &&
            registry.TryGetOwner(identity, new EntityStateDomain("command-lock"), out ReplicationOwner owner) &&
            owner.Id == CommandLockComponentAdapter.AdapterId;
    }

    private void OnCompleted(GenericActionResultMessage result)
    {
        if (_tracked.Contains(result.RequestId)) _completed.Add(result.Outcome);
    }

    private ScenarioVerdict Fail(string reason)
    {
        _fact("locks.failure", reason);
        if (_client != null) _client.GenericActionCompleted -= OnCompleted;
        return ScenarioVerdict.Fail;
    }

    private object[] Workers() => ((IEnumerable)typeof(GameFamilyAccess).GetMethod("CurrentChildren")!.Invoke(null, null)!).Cast<object>().ToArray();
    private ushort Index(object handle) => (ushort)_value(handle, "stableIndex")!;
    private bool Locked(object handle) => Invoke(Ownership, "IsCommandLocked", handle) is true;
    private bool Candidate(object handle) => Invoke(Ownership, "IsCommandLockCandidate", handle) is true;
    private bool HasOwnLocks() => _value(Service, "HasCommandLocks") is true;
    private object Ownership => _staticValue("GAT.World", "commandOwnershipService")!;
    private object Service => _staticValue("GAT.World", "commandService")!;
}
