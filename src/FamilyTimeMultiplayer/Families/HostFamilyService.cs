using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Families;
using FamilyTimeMultiplayer.Game;
using GAT;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using GatSimulation = global::GAT.GAT;

namespace FamilyTimeMultiplayer.Families;

public sealed class HostFamilyService : IDisposable
{
    private const double HungerSampleInterval = 1.0;
    private const int HungerBit = 1 << 19;
    private const int StarterSpawnRadius = 10;

    private readonly ManualLogSource _log;
    private readonly FamilyLedger _ledger = new FamilyLedger();
    private readonly Dictionary<int, string> _ownerByPlayerId = new Dictionary<int, string>();
    private readonly HashSet<ushort> _hungry = new HashSet<ushort>();
    private readonly string _hostOwner;
    private string? _spawnOwner;
    private int _layoutVersion;
    private bool _hungerReadbackInFlight;
    private double _nextHungerSample;
    private readonly Delegate _layoutChangedHandler;

    private readonly List<(string Owner, Vector3 Center, int Count)> _plannedGrowth = new List<(string, Vector3, int)>();
    private int _plannedHostGrowth;

    private HostFamilyService(ManualLogSource log, string hostOwner, FamilyWorldRecord? record)
    {
        _log = log;
        _hostOwner = record?.Families.SingleOrDefault(family => family.IsHostFamily)?.Owner ?? hostOwner;
        Identities = new Core.Families.FamilyIdentityRegistry(record ?? FamilyWorldRecord.Empty, _hostOwner);
        _ownerByPlayerId[Core.Protocol.PlayerRosterMessage.HostPlayerId] = _hostOwner;
        Mamas = new HostMamaService(log, this);
        AdoptChildren(record);
        if (record != null)
        {
            Mamas.Restore(record);
        }
        _layoutChangedHandler = GatInterop.SubscribeLayoutChanged(GatInterop.RequireAlive(World.gat, "World.gat is missing."), OnLayoutChanged);
        Current = this;
    }

    public static HostFamilyService? Current { get; private set; }

    public HostMamaService Mamas { get; }

    public Core.Families.FamilyIdentityRegistry Identities { get; }

    public string HostOwner => _hostOwner;

    public IReadOnlyCollection<ushort> MembersOf(string owner) => _ledger.MembersOf(owner);

    public static HostFamilyService EnsureStarted(ManualLogSource log, string hostOwner) =>
        Current ?? new HostFamilyService(log, hostOwner, null);

    public static HostFamilyService StartFromRecord(ManualLogSource log, string hostOwner, FamilyWorldRecord record)
    {
        if (Current != null)
        {
            throw new InvalidOperationException("Families are still active from the previous world; detach them before loading.");
        }
        return new HostFamilyService(log, hostOwner, record);
    }

    private void AdoptChildren(FamilyWorldRecord? record)
    {
        HashSet<ushort> children = GameFamilyAccess.CurrentChildren().Select(h => h.stableIndex).ToHashSet();
        if (record != null)
        {
            foreach (FamilyRecord family in record.Families)
            {
                string owner = family.IsHostFamily ? _hostOwner : family.Owner;
                foreach (ushort member in family.Members.Where(children.Contains))
                {
                    _ledger.Assign(member, owner);
                }
            }
        }
        int adopted = 0;
        foreach (ushort child in children.Where(c => !_ledger.TryGetOwner(c, out _)))
        {
            _ledger.Assign(child, _hostOwner);
            adopted++;
        }
        _log.LogInfo($"[Family] families: {string.Join(", ", _ledger.Owners.Select(o => $"'{o}'={_ledger.CountOf(o)}"))} ({adopted} unowned child(ren) given to the host)");
    }

    public FamilyWorldRecord ToRecord()
    {
        IEnumerable<string> owners = _ledger.Owners.Where(_ledger.HasFamily).Concat(Mamas.Owners).Concat(Identities.Owners).Append(_hostOwner).Distinct(StringComparer.Ordinal);
        return new FamilyWorldRecord(owners
            .Select(owner => new FamilyRecord(owner, owner == _hostOwner, _ledger.MembersOf(owner).ToArray(), Mamas.AgentOf(owner), Identities.PublicKeyFor(owner)))
            .ToArray());
    }

    public void DetachForWorldLoad()
    {
        Mamas.ForgetClones();
        Dispose();
    }

    public IReadOnlyDictionary<string, FamilyNightOutcome> PlanNight(IReadOnlyList<(string Owner, Vector3 Center)> sleepers)
    {
        _plannedGrowth.Clear();
        _plannedHostGrowth = 0;
        float multiplier = GameFamilyAccess.ChildGrowthMultiplier;
        var outcomes = new Dictionary<string, FamilyNightOutcome>(StringComparer.Ordinal);
        foreach ((string owner, Vector3 center) in sleepers)
        {
            FamilyNightOutcome outcome = NightRules.Evaluate(NightStateFor(owner), multiplier);
            outcomes[owner] = outcome;
            if (outcome.GrownBy > 0)
            {
                _plannedGrowth.Add((owner, center, outcome.GrownBy));
            }
            if (owner == _hostOwner)
            {
                _plannedHostGrowth = outcome.GrownBy;
            }
            _log.LogInfo($"[Night] '{owner}': missing={outcome.ChildrenMissing} hungry={outcome.ChildrenHungry} grows by {outcome.GrownBy}");
        }
        return outcomes;
    }

    public void RunPlannedGrowth(Action onComplete)
    {
        GameFamilyAccess.AnimateGrowth(_plannedHostGrowth, () =>
        {
            foreach ((string owner, Vector3 center, int count) in _plannedGrowth)
            {
                GrowFamily(owner, center, count);
            }
            _plannedGrowth.Clear();
            _plannedHostGrowth = 0;
            onComplete();
        });
    }

    public void RegisterPlayer(int playerId, string publicKey)
    {
        _ownerByPlayerId[playerId] = Identities.OwnerOf(publicKey) ?? throw new InvalidOperationException("Player has no authenticated family binding.");
    }

    public void UnregisterPlayer(int playerId)
    {
        _ownerByPlayerId.Remove(playerId);
    }

    public bool TryOwnerOfPlayer(int playerId, out string owner) => _ownerByPlayerId.TryGetValue(playerId, out owner);

    public string OwnerOfPlayer(int playerId) =>
        _ownerByPlayerId.TryGetValue(playerId, out string owner)
            ? owner
            : throw new InvalidOperationException($"Player #{playerId} has no family registered.");

    public string ActiveOwner =>
        CommanderContext.Active != null ? OwnerOfPlayer(CommanderContext.Active.PlayerId) : _hostOwner;

    public bool HasFamily(string owner) => _ledger.HasFamily(owner);

    public bool BelongsToActiveOwner(Handle handle) => _ledger.Belongs(handle.stableIndex, ActiveOwner);

    public Predicate<Handle> FilterActiveFamily(Predicate<Handle>? original)
    {
        string owner = ActiveOwner;
        return handle => _ledger.Belongs(handle.stableIndex, owner) && (original?.Invoke(handle) ?? true);
    }

    public Handle[] ExcludeOtherFamiliesFromWorkers(IEnumerable<Handle> unavailable) =>
        _ledger.ExcludedWorkersFor(ActiveOwner,
                GameFamilyAccess.CurrentChildren().Select(handle => handle.stableIndex),
                unavailable.Select(handle => handle.stableIndex))
            .Select(GatInterop.ToHandle).ToArray();

    public Handle[] FilterFollowers(FollowController follow, IEnumerable<Handle> followers)
    {
        int playerId = HostCommandHub.Current?.PlayerIdOf(follow) ?? Core.Protocol.PlayerRosterMessage.HostPlayerId;
        string owner = OwnerOfPlayer(playerId);
        return followers.Where(handle => _ledger.Belongs(handle.stableIndex, owner)).ToArray();
    }

    public int ChildrenOf(string owner) => _ledger.CountOf(owner);

    public float MeanDistanceOf(string owner, Vector3 from)
    {
        MapManager? map = World.mapManager;
        float total = 0f;
        int counted = 0;
        foreach (ushort member in _ledger.MembersOf(owner))
        {
            if (map != null && map.TryGetCachedPackedCell(member, out uint cell))
            {
                total += Vector3.Distance(Cell.Unpack(cell), from);
                counted++;
            }
        }
        return counted == 0 ? -1f : total / counted;
    }

    public int AvailableFor(string owner)
    {
        CommandOwnershipService? service = World.commandOwnershipService;
        if (service == null)
        {
            return 0;
        }
        int free = 0;
        foreach (ushort member in _ledger.MembersOf(owner))
        {
            if (GameFamilyAccess.IsFreeWorker(service, member))
            {
                free++;
            }
        }
        return free;
    }

    public FamilyHudCounts CountsFor(string owner)
    {
        int hungry = 0;
        int notSleeping = 0;
        IReadOnlyCollection<ushort> members = _ledger.MembersOf(owner);
        foreach (ushort member in members)
        {
            if (_hungry.Contains(member))
            {
                hungry++;
            }
            if (!GameFamilyAccess.IsSleeping(member))
            {
                notSleeping++;
            }
        }
        return new FamilyHudCounts(members.Count, hungry, notSleeping);
    }

    public FamilyNightState NightStateFor(string owner)
    {
        FamilyHudCounts counts = CountsFor(owner);
        return new FamilyNightState(counts.Children, counts.NotSleeping, counts.Hungry);
    }

    public void OnAgentsCreated(IReadOnlyList<Handle> handles, IReadOnlyList<int> templateIndices)
    {
        int childTemplate = GameFamilyAccess.RequireChildTemplateIndex();
        string owner = _spawnOwner ?? ActiveOwner;
        for (int i = 0; i < handles.Count; i++)
        {
            if (templateIndices[i] == childTemplate && !handles[i].isNull)
            {
                _ledger.Assign(handles[i].stableIndex, owner);
            }
        }
    }

    public void OnAgentsDeleted(IReadOnlyList<Handle> handles)
    {
        foreach (Handle handle in handles)
        {
            if (!handle.isNull && _ledger.Release(handle.stableIndex))
            {
                _hungry.Remove(handle.stableIndex);
            }
        }
    }

    public int SpawnStarterFamily(string owner, Vector3 center) =>
        SpawnFor(owner, center, GameFamilyAccess.ChildCountStart, StarterSpawnRadius, "starter family");

    public int GrowFamily(string owner, Vector3 center, int count) =>
        SpawnFor(owner, center, count, GameFamilyAccess.SpawnRadius, "night growth");

    private int SpawnFor(string owner, Vector3 center, int count, int radius, string reason)
    {
        if (_spawnOwner != null)
        {
            throw new InvalidOperationException($"Already spawning children for '{_spawnOwner}'.");
        }
        _spawnOwner = owner;
        Handle[] spawned;
        try
        {
            spawned = GameFamilyAccess.SpawnChildrenNear(center, count, radius);
        }
        finally
        {
            _spawnOwner = null;
        }
        foreach (Handle child in spawned)
        {
            if (!child.isNull)
            {
                _ledger.Assign(child.stableIndex, owner);
            }
        }
        _log.LogInfo($"[Family] {reason}: spawned {spawned.Length}/{count} child(ren) for '{owner}' near {center}; family now {_ledger.CountOf(owner)}");
        return spawned.Length;
    }

    public void Tick(double now)
    {
        if (now < _nextHungerSample || _hungerReadbackInFlight)
        {
            return;
        }
        _nextHungerSample = now + HungerSampleInterval;
        GatSimulation? gat = World.gat;
        ComputeBuffer? info = gat != null ? gat.GetInstanceInfoBuffer() : null;
        if (gat == null || info == null || gat.instanceCount == 0)
        {
            return;
        }
        (int start, int end) = GameFamilyAccess.ChildPhysicalRange();
        if (end <= start)
        {
            _hungry.Clear();
            return;
        }
        int layout = _layoutVersion;
        _hungerReadbackInFlight = true;
        AsyncGPUReadback.Request(info, (end - start) * sizeof(uint), start * sizeof(uint), request =>
        {
            _hungerReadbackInFlight = false;
            if (request.hasError || layout != _layoutVersion || Current != this || World.gat == null)
            {
                return;
            }
            NativeArray<uint> data = request.GetData<uint>();
            _hungry.Clear();
            for (int i = 0; i < data.Length; i++)
            {
                if ((data[i] & HungerBit) != 0)
                {
                    _hungry.Add(World.gat.GetHandle((ushort)(start + i)).stableIndex);
                }
            }
        });
    }

    private void OnLayoutChanged() => _layoutVersion++;

    public void Dispose()
    {
        Mamas.Dispose();
        if (World.gat != null)
        {
            GatInterop.UnsubscribeLayoutChanged(World.gat, _layoutChangedHandler);
        }
        if (Current == this)
        {
            Current = null;
        }
    }
}
