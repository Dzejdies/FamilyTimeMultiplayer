using System;
using System.Linq;
using System.Collections.Generic;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Commands;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Commands;

public readonly struct CommandResult
{
    public readonly CommandOutcome Outcome;
    public readonly string Detail;

    public CommandResult(CommandOutcome outcome, string detail)
    {
        Outcome = outcome;
        Detail = detail;
    }

    public static CommandResult Accepted(string detail) => new CommandResult(CommandOutcome.Accepted, detail);

    public static CommandResult Refused(string detail) => new CommandResult(CommandOutcome.Refused, detail);
}

public sealed class RemoteCommander : IRemoteCommanderIdentity, IDisposable
{
    private readonly ManualLogSource _log;
    private readonly CommandOwnershipService _ownership;
    private readonly GameObject _eyeObject;
    private readonly AttackCommandController _attack;
    private readonly StationCommandController _station;
    private readonly PickupWorkerScheduler _pickup;
    private readonly ConstructionScheduler _construction;
    private readonly FollowController _follow;
    private readonly RemoteCommandOutlines _outlines;
    private readonly PickupSelectionSweep _pickupSweep = new PickupSelectionSweep();
    private HostRigidRegistry? _sweepRigids;
    private FloodPathRequest? _goToRequest;
    private Vector3 _avatarPosition;

    public RemoteCommander(ManualLogSource log, int playerId, string playerName, Handle avatar)
    {
        _log = log;
        PlayerId = playerId;
        AvatarHandle = avatar;
        CommandService host = GatInterop.RequireAlive(World.commandService, "World.commandService is missing.");
        _ownership = GatInterop.RequireAlive(World.commandOwnershipService, "World.commandOwnershipService is missing.");
        Traverse config = Traverse.Create(host);
        Camera hostCamera = config.Field<Camera>("camera").Value;

        _eyeObject = new GameObject($"FTMP Eye ({playerName})");
        UnityEngine.Object.DontDestroyOnLoad(_eyeObject);
        Camera eye = _eyeObject.AddComponent<Camera>();
        eye.enabled = false;
        if (hostCamera != null)
        {
            eye.fieldOfView = hostCamera.fieldOfView;
            eye.nearClipPlane = hostCamera.nearClipPlane;
            eye.farClipPlane = hostCamera.farClipPlane;
        }
        Eye = _eyeObject.transform;

        float reach = GameApiCompatibility.CommandRaycastDistance(host, 30f);
        var visuals = new CommandVisuals(_eyeObject, eye, reach,
            config.Field<Mesh>("quadMesh").Value,
            config.Field<Material>("dropoffHudMaterial").Value,
            config.Field<Material>("gotoHudMaterial").Value,
            config.Field<Material>("constructionHudMaterial").Value,
            config.Field<Material>("attackHudMaterial").Value,
            config.Field<Material>("pickupHudMaterial").Value,
            config.Field<Material>("lockedHudMaterial").Value,
            config.Field<Color>("pickupHoverColor").Value,
            config.Field<Color>("queuedPickupColor").Value,
            config.Field<Color>("constructionColor").Value);
        _outlines = new RemoteCommandOutlines(visuals);
        float maxDistanceFromSource = config.Field<float>("maxDistanceFromSourceCommand").Value;
        _attack = new AttackCommandController(_ownership, CommandAudioCatalog.Get(CommandSound.Attack),
            CommandAudioCatalog.Get(CommandSound.QueueAdd), CommandAudioCatalog.Get(CommandSound.QueueRemove));
        _station = GameApiCompatibility.CreateStationController(World.gatSmartCombatBehavior, _ownership,
            maxDistanceFromSource);
        _pickup = new PickupWorkerScheduler(_ownership, visuals, CommandAudioCatalog.Get(CommandSound.Start),
            CommandAudioCatalog.Get(CommandSound.Stop), CommandAudioCatalog.Get(CommandSound.DropOff),
            CommandAudioCatalog.Get(CommandSound.QueueAdd), CommandAudioCatalog.Get(CommandSound.QueueRemove));
        _construction = GameApiCompatibility.CreateConstructionScheduler(_pickup, eye, CommandAudioCatalog.Get(CommandSound.Construction), reach, maxDistanceFromSource);
        _follow = new FollowController(_attack, _station, _pickup, _construction, _ownership, host.AutonomyManager,
            config.Field<float>("followPathRefreshTime").Value,
            config.Field<float>("followCursorMaxDistance").Value,
            config.Field<float>("followLongPressDuration").Value,
            CommandAudioCatalog.Get(CommandSound.FollowStart), CommandAudioCatalog.Get(CommandSound.FollowStop), eye);
        _attack.Follow = _follow;
        _station.Follow = _follow;
        _pickup.Attack = _attack;
        _pickup.Follow = _follow;
        _construction.Follow = _follow;
        _follow.onGoToCommand += OnGoToCommand;
        _follow.onFollowingStopped += StopTrackingGoTo;
        _follow.onFollowingAvailableAgentsStarted += FollowWithOwnMama;
        WolfMama.onAllowFollowChanged += OnMamaAllowFollowChanged;
        if (World.gat != null)
        {
            World.gat.onHandleDeleted += _follow.OnHandleDeleted;
        }
        UpdatePose(HostPlayerAuthority.RequirePose(PlayerId));
    }

    public int PlayerId { get; }

    public Handle AvatarHandle { get; private set; }

    public Vector3 AvatarPosition => _avatarPosition;

    public Transform Eye { get; }

    public FollowController Follow => _follow;

    public PickupWorkerScheduler Pickup => _pickup;

    public CommandOutlinesMessage CaptureOutlines(HostRigidRegistry rigids) => _outlines.Capture(rigids);

    public CommandMarkersMessage CaptureMarkers() => CommandMarkerCapture.Capture(_follow, _attack, _pickup, _construction);

    public int ReleasableFollowerCount => FollowGroups.ReleasableFollowers(_follow, _pickup).Count;

    public void UpdatePose(ClientPoseMessage pose)
    {
        pose = HostPlayerAuthority.RequireCommandPose(PlayerId);
        _avatarPosition = GatInterop.ToUnity(pose.Position);
        Eye.SetPositionAndRotation(_avatarPosition + Vector3.up * 1.7f, Quaternion.Euler(pose.PitchDegrees, pose.YawDegrees, 0f));
    }

    public void SuspendInputGestures()
    {
        StopPickupSweep();
        GameApiCompatibility.CancelFollowGesture(_follow);
    }

    public void Tick()
    {
        using (CommanderContext.Enter(this))
        {
            _pickup.Tick();
            UpdatePickupSweep();
            _station.Tick();
            _follow.UpdateFollow();
            GameApiCompatibility.ResumeConstruction(_construction);
        }
    }

    public int ReleaseFollowers(int count)
    {
        using (CommanderContext.Enter(this))
        {
            return FollowGroups.Release(_follow, _pickup, count);
        }
    }

    public void Handle(CommandIntentMessage intent, HostRigidRegistry rigids, Action<CommandResult> report)
    {
        UpdatePose(HostPlayerAuthority.RequirePose(PlayerId));
        ValidateIntent(intent, rigids);
        using (CommanderContext.Enter(this))
        {
            switch (intent.Kind)
            {
                case CommandIntentKind.FollowPressed:
                    _follow.OnFollowInputStarted();
                    break;
                case CommandIntentKind.FollowReleased:
                    _follow.OnFollowInputCanceled();
                    report(CommandResult.Accepted(_follow.IsFollowing ? "following" : "stopped following"));
                    break;
                case CommandIntentKind.Interact:
                    HandleInteract(intent, rigids, report);
                    break;
                case CommandIntentKind.CommandLock:
                    ToggleCommandLock(intent, report);
                    break;
                case CommandIntentKind.PickupSweepStarted:
                    BeginPickupSweep(intent, rigids);
                    break;
                case CommandIntentKind.PickupSweepStopped:
                    StopPickupSweep();
                    break;
            }
        }
    }

    private void ValidateIntent(CommandIntentMessage intent, HostRigidRegistry rigids)
    {
        if (intent.Kind == CommandIntentKind.FollowPressed || intent.Kind == CommandIntentKind.FollowReleased) return;
        if (intent.Kind == CommandIntentKind.PickupSweepStopped)
        {
            if (intent.TargetKind != CommandTargetKind.None || intent.RigidNetId != -1) throw new System.IO.InvalidDataException("Invalid pickup sweep stop.");
            return;
        }
        if (intent.Kind == CommandIntentKind.PickupSweepStarted && intent.TargetKind != CommandTargetKind.Rigid)
            throw new System.IO.InvalidDataException("Pickup sweep requires a rigid target.");
        float reach = HostPlayerAuthority.HandReach;
        Vector3 point;
        if (intent.Kind == CommandIntentKind.CommandLock || intent.TargetKind == CommandTargetKind.Agent)
        {
            if (intent.RigidNetId < 0 || intent.RigidNetId >= ushort.MaxValue) throw new System.IO.InvalidDataException("Invalid agent target.");
            point = HostPlayerAuthority.AgentPosition((ushort)intent.RigidNetId);
            if (intent.Kind == CommandIntentKind.CommandLock) HostPlayerAuthority.RequireOwnedAgent(PlayerId, (ushort)intent.RigidNetId);
            else if (World.gat.GetTemplateFromHandle(GatInterop.ToHandle((ushort)intent.RigidNetId))?.faction <= Faction.PLAYER) throw new System.IO.InvalidDataException("Target is not an attackable animal.");
        }
        else if (intent.TargetKind == CommandTargetKind.Rigid)
        {
            if (!rigids.TryResolve(intent.RigidNetId, out RigidTransform rigid)) throw new System.IO.InvalidDataException("Unknown rigid target.");
            HostPlayerAuthority.RequireRigidNear(PlayerId, rigid, reach + 3);
            return;
        }
        else if (intent.TargetKind == CommandTargetKind.Prop)
            point = new Vector3(intent.PropCellX, World.heightfield.GetScaledHeight(intent.PropCellX, intent.PropCellZ), intent.PropCellZ);
        else point = GatInterop.ToUnity(intent.Point);
        HostPlayerAuthority.RequireNear(PlayerId, point, reach + 3);
    }

    private void BeginPickupSweep(CommandIntentMessage intent, HostRigidRegistry rigids)
    {
        RigidTransform rigid = ResolvePickupSweepTarget(intent.RigidNetId, rigids);
        HostPlayerAuthority.RequireRigidNear(PlayerId, rigid, HostPlayerAuthority.HandRaycastDistance);
        _sweepRigids = rigids;
        if (_pickupSweep.Begin(intent.RigidNetId, _pickup.SelectedRigids.Contains(rigid))) _pickup.ToggleSelectedRigid(rigid);
    }

    private void UpdatePickupSweep()
    {
        if (!_pickupSweep.IsActive || _sweepRigids == null || !World.RaycastAll(Eye.position, Eye.forward, HostPlayerAuthority.HandRaycastDistance, out IReadOnlyList<World.HitInfo> hits)) return;
        foreach (World.HitInfo hit in hits)
        {
            if (hit.rigidId.HasValue && RigidTransform.TryGetById(hit.rigidId.Value, out RigidTransform rigid) && rigid != null && rigid.CommandShape != GATPickupBehavior.CommandShape.NONE)
            {
                if (_sweepRigids.TryGetNetId(rigid, out int netId) && _pickupSweep.TryVisit(netId, _pickup.SelectedRigids.Contains(rigid)))
                    _pickup.ToggleSelectedRigid(rigid);
            }
            else if (hit.blockId.HasValue || !hit.cpuCollisionType.HasValue) break;
        }
    }

    private static RigidTransform ResolvePickupSweepTarget(int rigidNetId, HostRigidRegistry rigids)
    {
        if (!rigids.TryResolve(rigidNetId, out RigidTransform rigid) || rigid.CommandShape == GATPickupBehavior.CommandShape.NONE)
            throw new System.IO.InvalidDataException("Pickup sweep target is not selectable.");
        return rigid;
    }

    private void StopPickupSweep()
    {
        _pickupSweep.Stop();
        _sweepRigids = null;
    }

    public void OnAgentGrabbed(Handle agent)
    {
        using (CommanderContext.Enter(this))
        {
            _follow.OnGATInstanceGrabbed(agent);
        }
    }

    private void OnMamaAllowFollowChanged(WolfMama changed)
    {
        if (Families.HostMamaService.Current == null || !Families.HostMamaService.Current.TryGetMamaOf(PlayerId, out WolfMama mama) || mama != changed)
        {
            return;
        }
        using (CommanderContext.Enter(this))
        {
            if (!_follow.IsFollowing)
            {
                return;
            }
            if (mama.AllowFollow && mama.TryGetHandle(out Handle handle))
            {
                _follow.StartFollowingWolfMama(handle);
            }
            else
            {
                _follow.StopFollowingWolfMama();
            }
        }
        _log.LogInfo($"[Mama] #{PlayerId}'s mama is now {(mama.AllowFollow ? "following" : "locked")}");
    }

    private void FollowWithOwnMama()
    {
        if (Families.HostMamaService.Current == null || !Families.HostMamaService.Current.TryGetMamaOf(PlayerId, out WolfMama mama))
        {
            return;
        }
        using (CommanderContext.Enter(this))
        {
            bool hasHandle = mama.TryGetHandle(out Handle handle);
            if (mama.AllowFollow && hasHandle)
            {
                _follow.StartFollowingWolfMama(handle);
            }
        }
    }

    public void Dispose()
    {
        StopPickupSweep();
        _outlines.Dispose();
        using (CommanderContext.Enter(this))
        {
            StopTrackingGoTo();
            _follow.onGoToCommand -= OnGoToCommand;
            _follow.onFollowingStopped -= StopTrackingGoTo;
            _follow.onFollowingAvailableAgentsStarted -= FollowWithOwnMama;
            WolfMama.onAllowFollowChanged -= OnMamaAllowFollowChanged;
            if (World.gat != null)
            {
                World.gat.onHandleDeleted -= _follow.OnHandleDeleted;
            }
            _attack.ClearForWorldLoad();
            _pickup.ClearForWorldLoad();
            _station.ClearForWorldLoad();
            _follow.ClearForWorldLoad();
            _construction.ClearForWorldLoad();
        }
        UnityEngine.Object.Destroy(_eyeObject);
    }

    private void HandleInteract(CommandIntentMessage intent, HostRigidRegistry rigids, Action<CommandResult> report)
    {
        if (_pickup.PrimaryClaimPending)
        {
            report(CommandResult.Refused("previous pickup is still assigning kids"));
            return;
        }
        switch (intent.TargetKind)
        {
            case CommandTargetKind.Rigid:
                if (!rigids.TryResolve(intent.RigidNetId, out RigidTransform rigid))
                {
                    report(CommandResult.Refused("that item no longer exists"));
                    return;
                }
                ReportPickup(_pickup.TryTogglePrimaryPickup(rigid), rigid.CommandShape, report);
                break;
            case CommandTargetKind.Prop:
                ResolvePropThenPickup(intent, report);
                break;
            case CommandTargetKind.Agent:
                Attack(intent, report);
                break;
            case CommandTargetKind.BuildPlan:
                AimEyeAt(GatInterop.ToUnity(intent.Point));
                BuildPlanManager.BuildPlan? plan = GameApiCompatibility.FindCommandPlan(Eye, GameApiCompatibility.CommandRaycastDistance(World.commandService, HostPlayerAuthority.HandReach));
                report(GameApiCompatibility.StartConstruction(_construction, plan)
                    ? CommandResult.Accepted("building")
                    : CommandResult.Refused($"cannot build that now ({_ownership.AvailableWorkerCount} free kids, materials must be nearby)"));
                break;
            case CommandTargetKind.Station:
                StartStation(GatInterop.ToUnity(intent.Point), report);
                break;
            case CommandTargetKind.Ground:
                Vector3 point = GatInterop.ToUnity(intent.Point);
                bool dropoff = (_pickup.SelectedCount > 0 || _pickup.HasActivePickup()) && _pickup.TryStartDropoffCommand(Vector3Int.FloorToInt(point), point);
                report(dropoff ? CommandResult.Accepted("drop-off") : CommandResult.Refused("nothing to drop off there"));
                break;
            default:
                report(CommandResult.Refused("nothing to interact with"));
                break;
        }
    }

    private static readonly System.Reflection.MethodInfo ToggleAttackTarget =
        AccessTools.Method(typeof(AttackCommandController), "ToggleTarget")
        ?? throw new MissingMethodException(nameof(AttackCommandController), "ToggleTarget");

    private readonly List<GATStation> _stationScratch = new List<GATStation>();

    private void Attack(CommandIntentMessage intent, Action<CommandResult> report)
    {
        Handle target = GatInterop.ToHandle((ushort)intent.RigidNetId);
        if (intent.RigidNetId < 0 || intent.RigidNetId >= ushort.MaxValue || World.gat == null || !World.gat.IsHandleValid(target))
        {
            report(CommandResult.Refused("that animal is gone"));
            return;
        }
        if (World.gatCommandManager?.GetCommand<GATAttackBehavior>() == null)
        {
            report(CommandResult.Refused("attacking is not available"));
            return;
        }
        ToggleAttackTarget.Invoke(_attack, new object[] { target, HostPlayerAuthority.AgentPosition(target.stableIndex) });
        GameApiCompatibility.StopAvailableFollowers(_follow);
        _attack.Rebalance();
        _pickup.ClearSelectedRigids();
        report(CommandResult.Accepted("attack"));
    }

    private void AimEyeAt(Vector3 point)
    {
        Vector3 direction = point - Eye.position;
        if (direction.sqrMagnitude > 1e-4f)
        {
            Eye.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }

    private void StartStation(Vector3 position, Action<CommandResult> report)
    {
        _stationScratch.Clear();
        GATStation.GetStationsInSphere(position, 1f, _stationScratch);
        GATStation? station = _stationScratch.OrderBy(s => (s.Position - position).sqrMagnitude).FirstOrDefault();
        if (station == null)
        {
            report(CommandResult.Refused("that bed/station is gone"));
            return;
        }
        if (_station.StartNearbyStationCommands(station, station.Position))
        {
            _pickup.ClearSelectedRigids();
            report(CommandResult.Accepted(station.IsSleepStation ? "kids go to bed" : "kids go to the station"));
        }
        else
        {
            report(CommandResult.Refused($"no free kids for that ({_ownership.AvailableWorkerCount} free)"));
        }
    }

    private void ToggleCommandLock(CommandIntentMessage intent, Action<CommandResult> report)
    {
        Handle target = GatInterop.ToHandle((ushort)intent.RigidNetId);
        if (intent.RigidNetId < 0 || intent.RigidNetId >= ushort.MaxValue || !_ownership.IsCommandLockCandidate(target))
        {
            report(CommandResult.Refused("that cannot be locked"));
            return;
        }
        bool locked = !_ownership.IsCommandLocked(target);
        if (_ownership.SetCommandLocked(target, locked))
        {
            CommandAudioCatalog.PlayNative(CommandAudioCatalog.ForLockState(locked));
            report(CommandResult.Accepted(locked ? "locked" : "unlocked"));
        }
        else
        {
            report(CommandResult.Refused("lock did not change"));
        }
    }

    private void ResolvePropThenPickup(CommandIntentMessage intent, Action<CommandResult> report)
    {
        PropChunkManager props = PropReplication.RequireManager();
        EnvironmentMesh expected = PropReplication.ResolveProp(intent.PropName);
        var cells = new List<uint> { Cell.Pack(new Vector3Int(intent.PropCellX, 0, intent.PropCellZ)) };
        props.GetPropCollisionInstances(cells, instances =>
        {
            if (!HostPlayerAuthority.Exists(PlayerId)) return;
            try { HostPlayerAuthority.RequireNear(PlayerId, new Vector3(intent.PropCellX, World.heightfield.GetScaledHeight(intent.PropCellX, intent.PropCellZ), intent.PropCellZ), HostPlayerAuthority.HandReach + 3); }
            catch (System.IO.InvalidDataException) { report(CommandResult.Refused("target moved out of reach")); return; }
            using (CommanderContext.Enter(this))
            {
                foreach (PropChunkManager.PropCollisionInstance instance in instances)
                {
                    if (instance.worldX != intent.PropCellX || instance.worldZ != intent.PropCellZ || instance.prop != expected)
                    {
                        continue;
                    }
                    object? prefab = instance.prop.pickupRigidPrefab;
                    if (prefab == null)
                    {
                        report(CommandResult.Refused("that cannot be picked up"));
                        return;
                    }
                    ReportPickup(GameApiCompatibility.StartPrimaryPickup(_pickup, instance, prefab), GameApiCompatibility.PickupCommandShape(prefab), report);
                    return;
                }
                report(CommandResult.Refused("that is gone already"));
            }
        });
    }

    private void ReportPickup(bool started, GATPickupBehavior.CommandShape shape, Action<CommandResult> report)
    {
        int required = GATPickupBehavior.RequiredAgents(shape);
        if (started)
        {
            _pickup.ClearSelectedRigids();
            report(CommandResult.Accepted($"pickup with {required} kids"));
        }
        else
        {
            report(CommandResult.Refused($"needs {required} free kids, {_ownership.AvailableWorkerCount} free"));
        }
    }

    private void OnGoToCommand()
    {
        StopTrackingGoTo();
        _pickup.PrepareCarryingWorkersForFollow();
        GameApiCompatibility.StartCarryingWorkersFollow(_pickup);
        _follow.AddAvailableAgentsToGoTo();
        FloodPathRequest request = _follow.FollowFlowFieldRequest;
        if (_follow.IsFollowing && request != null && !request.isDisposed && FloodRequests.HasAssignedAgents(request))
        {
            _goToRequest = request;
            _goToRequest.OnAgentReachedEnd += OnGoToAgentReachedEnd;
        }
    }

    private void StopTrackingGoTo()
    {
        if (_goToRequest != null)
        {
            _goToRequest.OnAgentReachedEnd -= OnGoToAgentReachedEnd;
            _goToRequest = null;
        }
    }

    private void OnGoToAgentReachedEnd(Handle handle)
    {
        using (CommanderContext.Enter(this))
        {
            if (_goToRequest == null || _goToRequest.isDisposed || _goToRequest != _follow.FollowFlowFieldRequest)
            {
                StopTrackingGoTo();
                return;
            }
            _follow.StopFollowingAgent(handle);
            World.gatUtilities?.SetInstancesScreenIconIndex(new[] { handle }, ScreenIcon.NONE);
            if (!FloodRequests.HasAssignedAgents(_goToRequest))
            {
                StopTrackingGoTo();
                _follow.StopFollowing(playSound: false);
            }
        }
    }
}

internal static class FloodRequests
{
    private static readonly System.Reflection.PropertyInfo AssignedAgents =
        AccessTools.Property(typeof(FloodPathRequest), "HasAssignedAgents")
        ?? throw new MissingMemberException(nameof(FloodPathRequest), "HasAssignedAgents");

    public static bool HasAssignedAgents(FloodPathRequest request) => (bool)AssignedAgents.GetValue(request);
}
