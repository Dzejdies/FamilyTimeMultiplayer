using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using GAT;
using UnityEngine;
using RigidTransform = GAT.RigidTransform;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class MultiplayerCommandScenario : ScenarioRunner
{
    private const string LogPrefabName = "rt_log01";

    private readonly ManualLogSource _log;
    private readonly List<(double At, string Name, Action<ISession> Step)> _timeline = new List<(double, string, Action<ISession>)>();
    private double _start = -1;
    private int _next;
    private double _nextProbe;

    private readonly bool _singlePlayerControl;

    public static MultiplayerCommandScenario SinglePlayerControl(ManualLogSource log) => new MultiplayerCommandScenario(log, singlePlayerControl: true);

    private MultiplayerCommandScenario(ManualLogSource log, bool singlePlayerControl)
    {
        _log = log;
        _singlePlayerControl = singlePlayerControl;
        Always(4, "spawn two logs", SpawnLogs);
        Always(10, "PICKUP log A", () => HostPickup(lowestIndex: true));
        Always(40, "DROPOFF log A", HostDropoff);
    }

    public MultiplayerCommandScenario(ManualLogSource log)
    {
        _log = log;
        Host(4, "spawn two logs", _ => SpawnLogs());
        Host(8, "host FOLLOW", _ => HostFollow(start: true));
        Client(11, "client FOLLOW", _ => ClientFollowToggle());
        Host(20, "host STOP follow", _ => HostFollow(start: false));
        Client(20, "client STOP follow", _ => ClientFollowToggle());
        Host(24, "host PICKUP log A (single-player baseline)", _ => HostPickup(lowestIndex: true));
        Client(27, "client PICKUP log B while kids busy (expect refused)", _ => ClientPickup(lowestIndex: false));
        Host(35, "host DROPOFF log A", _ => HostDropoff());
        Client(60, "client PICKUP log B again (expect accepted)", _ => ClientPickup(lowestIndex: false));
        Client(80, "client DROPOFF log B next to client", _ => ClientDropoff());
    }

    public static MultiplayerCommandScenario SeparateFamilies(ManualLogSource log) => new MultiplayerCommandScenario(log, BuildFamilyTimeline);

    private MultiplayerCommandScenario(ManualLogSource log, Action<MultiplayerCommandScenario> buildTimeline)
    {
        _log = log;
        buildTimeline(this);
    }

    private static void BuildFamilyTimeline(MultiplayerCommandScenario s)
    {
        s.Host(4, "spawn two logs", _ => s.SpawnLogs());
        s.Client(8, "client walks 12 m away from the host", _ => s.ClientMovesAway());
        s.Client(12, "client FOLLOW (own kids only)", _ => ClientFollowToggle());
        s.Host(16, "host FOLLOW (own kids only)", _ => s.HostFollow(start: true));
        s.Client(24, "client STOP follow", _ => ClientFollowToggle());
        s.Host(24, "host STOP follow", _ => s.HostFollow(start: false));
        s.Host(28, "host PICKUP log A", _ => s.HostPickup(lowestIndex: true));
        s.Client(31, "client PICKUP log B with own kids (expect accepted)", _ => s.ClientPickup(lowestIndex: false));
        s.Host(40, "grow client family by 2 (diagnostic)", _ => Families.HostFamilyService.Current!.GrowFamily("Client", LocalPlayer.Position, 2));
        s.Host(43, "host saves the world as ftmp_family_test", _ => World.worldSerializer!.Save("ftmp_family_test", ok => s._log.LogInfo($"[Scenario] save ftmp_family_test: {ok}")));
        s.Client(45, "client goes to bed", _ => Families.ClientNight.GoToBed());
        s.Host(50, "host goes to bed (night should start)", _ => s.HostGoesToBed());
    }

    private void ClientMovesAway()
    {
        Vector3 target = Families.GameFamilyAccess.TryFindWalkableNear(LocalPlayer.Position + new Vector3(12f, 0f, 0f), 2, out Vector3 walkable)
            ? walkable
            : throw new InvalidOperationException("No walkable cell 12 m from the client.");
        World.playerController!.TeleportTo(target + Vector3.up, Vector3.forward);
        _log.LogInfo($"[Scenario] client moved to {target}");
    }

    public static MultiplayerCommandScenario Holding(ManualLogSource log) => new MultiplayerCommandScenario(log, BuildHoldTimeline);

    private static void BuildHoldTimeline(MultiplayerCommandScenario s)
    {
        s.Host(4, "spawn two logs", _ => s.SpawnLogs());
        s.Client(10, "client grabs log B", _ => s.HoldStep(grab: true));
        s.Host(13, "host tries to grab log B (expect refused)", _ => s.HostGrabsHeldLog());
        s.Client(15, "client walks 6 m carrying it", _ => s.ClientMoves(6f));
        s.Client(20, "client drops log B", _ => s.HoldStep(grab: false));
        s.Client(30, "client locks the host's mama", _ => s.ClientTogglesMama());
        s.Client(34, "client picks up the host's mama", _ => s.MamaStep(grab: true));
        s.Client(36, "client walks 5 m carrying mama", _ => s.ClientMoves(5f));
        s.Client(40, "client puts mama down", _ => s.MamaStep(grab: false));
        s.Client(44, "client unlocks the host's mama", _ => s.ClientTogglesMama());
        s.Client(48, "client picks up a child", _ => s.ChildStep(grab: true));
        s.Host(51, "host tries to take that child (expect refused)", _ => s.HostGrabsHeldChild());
        s.Client(53, "client walks 6 m carrying the child", _ => s.ClientMoves(6f));
        s.Client(57, "client puts the child down", _ => s.ChildStep(grab: false));
        s.Client(63, "client reports the child", _ => s.ReportChild());
        s.Host(63, "host reports the child", _ => s.ReportChild());
        s.Client(66, "client grabs log B again", _ => s.HoldStep(grab: true));
        s.Host(69, "host destroys the held log (used as material)", _ => HostRigidRegistry.RequireManager().Delete(s.LogB()));
        s.Always(76, "session still alive", () => s._log.LogInfo($"[Scenario] alive check: session={MultiplayerController.Current?.Session?.Status ?? MultiplayerController.Current?.LastError}"));
        s.Host(80, "host stops hosting", _ => MultiplayerController.Current!.Leave());
        s._timeline.Add((80, "post-leave check scheduled", _ => { }));
    }

    private Handle _child = Handle.Null;
    private ushort _childHost = ushort.MaxValue;

    private void ChildStep(bool grab)
    {
        if (grab)
        {
            Vector3 me = LocalPlayer.Position;
            _child = Families.GameFamilyAccess.CurrentChildren()
                .OrderBy(h => World.gat!.GetInstanceMatrixImmediate(h) is Matrix4x4 m ? Vector3.Distance((Vector3)m.GetColumn(3), me) : float.MaxValue)
                .First();
            Holds.HandActions.GrabAgent(_child);
            _log.LogInfo($"[Scenario] client picked up child {_child}, carrying={Holds.HandActions.RequireHand().IsHandCarryingGAT(_child)}");
        }
        else
        {
            Holds.HandActions.DropAgent(_child);
            _log.LogInfo($"[Scenario] client put down child {_child} at {World.gat!.GetInstanceMatrixImmediate(_child)?.GetColumn(3)}");
        }
    }

    private void HostGrabsHeldChild()
    {
        Handle held = Families.GameFamilyAccess.CurrentChildren().FirstOrDefault(h => Holds.HostHoldRegistry.Current?.HolderOf(h) != null);
        if (held.isNull && Families.GameFamilyAccess.CurrentChildren().Count == 0)
        {
            throw new InvalidOperationException("No children on the host.");
        }
        _childHost = held.stableIndex;
        Holds.HandActions.GrabAgent(held);
        _log.LogInfo($"[Scenario] host grab attempt on child {held}: host carrying={Holds.HandActions.RequireHand().IsHandCarryingGAT(held)} notice='{UI.PlayerNotice.Current}'");
    }

    private void ReportChild()
    {
        Handle child = ReplicationGate.Role == SessionRole.Host ? GatInterop.ToHandle(_childHost) : _child;
        Vector3? at = child.isNull ? null : World.gat!.GetInstanceMatrixImmediate(child)?.GetColumn(3);
        _log.LogInfo($"[Scenario] child {child} is at {at} (me at {LocalPlayer.Position})");
    }

    private RigidTransform LogB()
    {
        RigidTransformManager manager = HostRigidRegistry.RequireManager();
        return Enumerable.Range(0, RigidTransformApi.Count(manager))
            .Select(i => RigidTransformApi.TryGetManagedRigid(manager, i, out RigidTransform r) ? r : null)
            .Where(r => RigidTransformApi.IsAlive(r) && RigidTransformApi.Name(r).StartsWith(LogPrefabName))
            .OrderBy(r => r!.position.x)
            .First()!;
    }

    private void HoldStep(bool grab)
    {
        RigidTransform log = LogB();
        if (grab)
        {
            Holds.HandActions.Grab(log);
        }
        else
        {
            Holds.HandActions.Drop(log);
        }
        _log.LogInfo($"[Scenario] client {(grab ? "grabbed" : "dropped")} {RigidTransformApi.Name(log)} at {log.position}, holding={Holds.HandActions.IsHolding(log)}");
    }

    private void HostGrabsHeldLog()
    {
        RigidTransform log = LogB();
        Holds.HandActions.Grab(log);
        _log.LogInfo($"[Scenario] host grab attempt on {RigidTransformApi.Name(log)}: host holding={Holds.HandActions.IsHolding(log)} notice='{UI.PlayerNotice.Current}'");
    }

    private void ClientMoves(float meters)
    {
        Vector3 target = Families.GameFamilyAccess.TryFindWalkableNear(LocalPlayer.Position + new Vector3(meters, 0f, 0f), 1, out Vector3 walkable)
            ? walkable
            : throw new InvalidOperationException($"No walkable cell {meters} m from the client.");
        World.playerController!.TeleportTo(target + Vector3.up, Vector3.forward);
        _log.LogInfo($"[Scenario] client moved to {target}");
    }

    private void ClientTogglesMama()
    {
        WolfMama mama = WolfMama.ActiveInstance ?? throw new InvalidOperationException("No mama on the client.");
        GameApiCompatibility.ToggleMama(mama);
        _log.LogInfo($"[Scenario] client toggled {mama.name}");
    }

    private void MamaStep(bool grab)
    {
        WolfMama mama = WolfMama.ActiveInstance ?? throw new InvalidOperationException("No mama on the client.");
        RigidTransform rigid = RigidTransformApi.FromMama(mama) ?? throw new MissingMemberException(nameof(WolfMama), "rigid transform");
        if (grab)
        {
            Holds.HandActions.Grab(rigid);
        }
        else
        {
            Holds.HandActions.Drop(rigid);
        }
        _log.LogInfo($"[Scenario] client {(grab ? "picked up" : "put down")} {mama.name} at {rigid.position}");
    }

    private void HostGoesToBed()
    {
        BedManager bed = UnityEngine.Object.FindFirstObjectByType<BedManager>()
            ?? throw new InvalidOperationException("No BedManager in the scene.");
        HarmonyLib.AccessTools.Method(typeof(BedManager), "BeginSleep").Invoke(bed, null);
    }

    private double _leftAt = -1;

    public override void Tick(double now, ISession? session)
    {
        if (_start >= 0 && session == null && MultiplayerController.Current?.Session == null && ReplicationGate.Role == SessionRole.Offline && _leftAt < 0 && now - _start > 79)
        {
            _leftAt = now;
        }
        if (_leftAt > 0 && now - _leftAt > 3)
        {
            _leftAt = double.MaxValue;
            _log.LogInfo($"[Scenario] after leave: session={MultiplayerController.Current?.Session?.Status ?? "none"} commanderContext={Commands.CommanderContext.Active?.PlayerId.ToString() ?? "none"} hand={(PlayerController.hand != null ? "ok" : "MISSING")} possessed={LocalPlayer.PossessedHandle} error='{MultiplayerController.Current?.LastError}'");
        }
        if (_singlePlayerControl)
        {
            TickControl(now);
            return;
        }
        if (session == null || !LocalPlayer.IsWorldReady || session.Roster.Count < 2)
        {
            return;
        }
        if (session is ClientSession && !ClientCommandBridge.IsActive)
        {
            return;
        }
        if (session.Role == SessionRole.Host && (HostCommandHub.Current == null || HostCommandHub.Current.FollowerCounts().Count < 2))
        {
            return;
        }
        if (_start < 0)
        {
            _start = now;
            _log.LogInfo($"[Scenario] mp-commands timeline started as {session.Role}");
        }
        while (_next < _timeline.Count && now - _start >= _timeline[_next].At)
        {
            (double _, string name, Action<ISession> step) = _timeline[_next++];
            _log.LogInfo($"[Scenario] step '{name}'");
            step(session);
        }
        if (now >= _nextProbe)
        {
            _nextProbe = now + 2.0;
            LogPool("probe");
        }
    }

    private void TickControl(double now)
    {
        if (!LocalPlayer.IsWorldReady || World.commandService == null)
        {
            return;
        }
        if (_start < 0)
        {
            _start = now;
        }
        while (_next < _timeline.Count && now - _start >= _timeline[_next].At)
        {
            (double _, string name, Action<ISession> step) = _timeline[_next++];
            _log.LogInfo($"[Scenario] step '{name}'");
            step(null!);
        }
        if (now >= _nextProbe)
        {
            _nextProbe = now + 2.0;
            LogPool("control");
        }
    }

    private void Always(double at, string name, Action step) => _timeline.Add((at, name, _ => step()));

    private void Host(double at, string name, Action<ISession> step) =>
        _timeline.Add((at, name, session => { if (session.Role == SessionRole.Host) { step(session); } }));

    private void Client(double at, string name, Action<ISession> step) =>
        _timeline.Add((at, name, session => { if (session.Role == SessionRole.Client) { step(session); } }));

    private void SpawnLogs()
    {
        RigidTransformManager manager = HostRigidRegistry.RequireManager();
        RigidPrefab prefab = Enumerable.Range(0, manager.PrefabCount)
            .Select(i => RigidTransformApi.TryGetPrefab(manager, i, out RigidPrefab p) ? p : null)
            .First(p => p != null && RigidTransformApi.PrefabName(p) == LogPrefabName)!;
        Vector3 origin = LocalPlayer.Position;
        RigidTransformApi.Spawn(manager, prefab, OnGround(origin + new Vector3(5f, 0f, 0f)), Quaternion.identity);
        RigidTransformApi.Spawn(manager, prefab, OnGround(origin + new Vector3(-5f, 0f, 0f)), Quaternion.identity);
        _log.LogInfo($"[Scenario] spawned 2 logs near {origin}; world items now {RigidTransformApi.Count(manager)}");
    }

    private static Vector3 OnGround(Vector3 position)
    {
        Heightfield heightfield = GatInterop.RequireAlive(World.heightfield, "World.heightfield is missing.");
        float ground = heightfield.GetWorldHeightAtQuadCenter(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.z));
        return new Vector3(position.x, ground + 0.6f, position.z);
    }

    private void HostFollow(bool start)
    {
        (FollowController follow, _) = HostCommandHub.HostControllers();
        if (start)
        {
            follow.StartFollowing();
        }
        else
        {
            follow.StopFollowing(playSound: false);
        }
    }

    private static void ClientFollowToggle()
    {
        ClientCommandBridge.Send(CommandIntentMessage.Follow(pressed: true));
        ClientCommandBridge.Send(CommandIntentMessage.Follow(pressed: false));
    }

    private void ClientPickup(bool lowestIndex)
    {
        RigidTransform log = FindLog(lowestIndex);
        if (!(World.commandService != null && TryGetClientNetId(log, out int netId)))
        {
            _log.LogError("[Scenario] client cannot resolve the log's net id");
            return;
        }
        ClientCommandBridge.Send(CommandIntentMessage.InteractRigid(netId));
    }

    private void HostPickup(bool lowestIndex)
    {
        (_, PickupWorkerScheduler pickup) = HostCommandHub.HostControllers();
        RigidTransform log = FindLog(lowestIndex);
        int before = World.commandOwnershipService!.AvailableWorkerCount;
        bool started = pickup.TryTogglePrimaryPickup(log);
        _log.LogInfo($"[Scenario] host pickup of log (needs {GATPickupBehavior.RequiredAgents(log.CommandShape)}): started={started}, free before={before}");
    }

    private void HostDropoff()
    {
        (_, PickupWorkerScheduler pickup) = HostCommandHub.HostControllers();
        Vector3 target = OnGround(LocalPlayer.Position + new Vector3(0f, 0f, -6f));
        bool started = pickup.TryStartDropoffCommand(Vector3Int.FloorToInt(target), target);
        _log.LogInfo($"[Scenario] host drop-off at {target}: started={started}");
    }

    private void ClientDropoff()
    {
        Vector3 target = OnGround(LocalPlayer.Position + new Vector3(0f, 0f, 6f));
        ClientCommandBridge.Send(CommandIntentMessage.InteractGround(GatInterop.ToNet(target)));
        _log.LogInfo($"[Scenario] client drop-off requested at {target}");
    }

    private static RigidTransform FindLog(bool lowestIndex)
    {
        RigidTransformManager manager = HostRigidRegistry.RequireManager();
        var logs = new List<RigidTransform>();
        for (int i = 0; i < RigidTransformApi.Count(manager); i++)
        {
            if (RigidTransformApi.TryGetManagedRigid(manager, i, out RigidTransform rigid, out int prefabIndex)
                && RigidTransformApi.TryGetPrefab(manager, prefabIndex, out RigidPrefab prefab) && RigidTransformApi.PrefabName(prefab) == LogPrefabName)
            {
                logs.Add(rigid);
            }
        }
        if (logs.Count < 2)
        {
            throw new InvalidOperationException($"Expected 2 logs in the world, found {logs.Count}.");
        }
        return lowestIndex ? logs[0] : logs[logs.Count - 1];
    }

    private static IEnumerable<string> LogPositions()
    {
        RigidTransformManager? manager = World.rigidTransformManager;
        if (manager == null)
        {
            yield break;
        }
        for (int i = 0; i < RigidTransformApi.Count(manager); i++)
        {
            if (RigidTransformApi.TryGetManagedRigid(manager, i, out RigidTransform rigid, out int prefabIndex)
                && RigidTransformApi.TryGetPrefab(manager, prefabIndex, out RigidPrefab prefab) && RigidTransformApi.PrefabName(prefab) == LogPrefabName)
            {
                yield return $"{rigid.position}{(rigid.IsCarried ? "*carried" : string.Empty)}";
            }
        }
    }

    private static bool TryGetClientNetId(RigidTransform rigid, out int netId)
    {
        netId = -1;
        return ClientCommandBridge.TryGetNetId(rigid, out netId);
    }

    private void LogPool(string label)
    {
        CommandOwnershipService? ownership = World.commandOwnershipService;
        if (ownership == null)
        {
            return;
        }
        string followers = HostCommandHub.Current != null
            ? string.Join(" ", HostCommandHub.Current.FollowerCounts().Select(entry => $"#{entry.Key}={entry.Value}"))
            : "n/a";
        string logs = string.Join(" ", LogPositions());
        Families.FamilyHudCounts? hud = Families.FamilyHud.Override;
        string family = hud.HasValue ? $"{hud.Value.Children} kids/{hud.Value.Hungry} hungry/{hud.Value.NotSleeping} awake" : "none";
        string mamas = string.Join(" ", UnityEngine.Object.FindObjectsByType<WolfMama>(FindObjectsSortMode.None)
            .OrderBy(m => m.name)
            .Select(m => $"{m.name}@{Vector3.Distance(m.transform.position, LocalPlayer.Position):0.0}m/agent{m.FakeGATHandle.stableIndex}/{(m.AllowFollow ? "follow" : "LOCKED")}/{m.transform.position}"));
        _log.LogInfo($"[Mama] {label}: {mamas}");
        _log.LogInfo($"[Mama] {label}: proximity {string.Join(" ", Patches.MamaProximityRotation.Stats.Select(e => $"{e.Key}={e.Value.Samples}s/{e.Value.Looks}l"))}");
        Families.HostFamilyService? families = Families.HostFamilyService.Current;
        ISession? session = MultiplayerController.Current?.Session;
        if (families != null && session != null)
        {
            string kids = string.Join(" ", session.Roster.Select(entry =>
            {
                Vector3 at = entry.PlayerId == session.LocalPlayerId ? LocalPlayer.Position : session.TryGetRemotePlayerPosition(entry, out Vector3 p) ? p : Vector3.zero;
                string owner = families.OwnerOfPlayer(entry.PlayerId);
                return $"{owner}:{families.MeanDistanceOf(owner, at):0.0}m";
            }));
            _log.LogInfo($"[Kids] {label}: mean distance to owner {kids}");
            string mamaFollow = string.Join(" ", HostCommandHub.Current!.FollowControllers().Select(f => $"#{f.PlayerId}={HarmonyLib.Traverse.Create(f.Follow).Field<Handle>("_wolfMamaHandle").Value}"));
            _log.LogInfo($"[Mama] {label}: following mama {mamaFollow}");
        }
        _log.LogInfo($"[Pool] {label}: free={ownership.AvailableWorkerCount}/{ownership.WorkerTemplateAgentCount} followers[{followers}] hud[{family}] logs[{logs}] notice='{UI.PlayerNotice.Current}'");
    }
}
