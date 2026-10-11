using System;
using System.Linq;
using System.Collections.Generic;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Interpolation;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Transport;
using FamilyTimeMultiplayer.Game;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.Sessions;

public sealed class ClientSession : ISession, IClientCommandSink, Families.IClientBedSink, Holds.IClientHoldSink
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private const int MaxConnectAttempts = 15;
    private const double ConnectRetryDelaySeconds = 2.0;

    private enum ClientPhase
    {
        Connecting,
        AwaitingWelcome,
        ReceivingWorld,
        WaitingToLoad,
        Loading,
        Live,
        Finished,
    }

    private readonly ManualLogSource _log;
    private readonly MultiplayerSettings _settings;
    private readonly Core.Security.PlayerIdentity _identity;
    private bool _identityChallengeReceived;
    private HelloMessage? _identityHello;
    private Addons.SessionAddonRuntime? _addons;
    private double _addonHostClockOffset;
    private readonly string _endpointDescription;
    private readonly JoinTarget _target;
    private PendingConnect _pendingConnect;
    private int _connectAttempts = 1;
    private double _retryAt = -1;
    private readonly SnapshotInterpolator<ushort> _interpolator;
    private readonly ClientRigidRegistry _rigids;
    private RigidIndexMapMessage? _pendingRigidMap;
    private readonly AgentPuppeteer _puppeteer = new AgentPuppeteer();
    private readonly ReplicatedAgentRegistry _registry;
    private readonly Families.ClientMamaMirror _mamas;
    private readonly DayStateBridge _day = new DayStateBridge();
    private readonly ClientWaterMirror _water = new ClientWaterMirror();
    private readonly LocalPoseTracker _pose = new LocalPoseTracker();
    private readonly List<InterpolatedEntry<ushort>> _sampled = new List<InterpolatedEntry<ushort>>();
    private INetConnection? _connection;
    private ClientPhase _phase = ClientPhase.Connecting;
    private IncomingWorldFile? _incomingWorld;
    private int _worldLoadGeneration;
    private WelcomeMessage? _welcome;
    private double _nextPoseTime;
    private bool _poseInitialized;
    private bool _addonPoseBound;
    private IReadOnlyList<RosterEntry> _roster = Array.Empty<RosterEntry>();

    public ClientSession(ManualLogSource log, MultiplayerSettings settings, JoinTarget target)
    {
        _log = log;
        _settings = settings;
        _identity = LocalIdentityStore.LoadOrCreate();
        _target = target;
        _endpointDescription = target.Description;
        _interpolator = new SnapshotInterpolator<ushort>(settings.InterpolationDelaySeconds);
        _rigids = new ClientRigidRegistry(settings.InterpolationDelaySeconds);
        _registry = new ReplicatedAgentRegistry(_puppeteer);
        _mamas = new Families.ClientMamaMirror(log);
        _pendingConnect = target.Start(ConnectTimeout);
        Status = $"Connecting to {_endpointDescription}...";
        _log.LogInfo($"[Client] connecting to {_endpointDescription}");
    }

    public SessionRole Role => SessionRole.Client;

    public string Status { get; private set; }

    public bool IsFinished => _phase == ClientPhase.Finished;

    public bool IsLive => _phase == ClientPhase.Live;

    public IReadOnlyList<RosterEntry> Roster => _roster;

    public int LocalPlayerId => _welcome?.PlayerId ?? -1;

    public FamilyMode? FamilyMode => _welcome?.FamilyMode;

    public ushort AvatarStableIndex => _welcome?.AvatarStableIndex ?? ushort.MaxValue;

    public AgentIdentityMap Identities => _registry.Identities;

    public int LastPuppetCount => _puppeteer.LastAppliedCount;

    public bool TryGetRemotePlayerPosition(RosterEntry player, out Vector3 position)
    {
        for (int i = 0; i < _sampled.Count; i++)
        {
            if (_sampled[i].Key == player.AvatarStableIndex)
            {
                position = GatInterop.ToUnity(_sampled[i].Pose.Position);
                return true;
            }
        }
        position = Vector3.zero;
        return false;
    }

    public bool TryGetRemotePlayerYaw(RosterEntry player, out float yawDegrees)
    {
        for (int i = 0; i < _sampled.Count; i++)
        {
            if (_sampled[i].Key == player.AvatarStableIndex)
            {
                yawDegrees = GatInterop.ToUnity(_sampled[i].Pose.Rotation).eulerAngles.y;
                return true;
            }
        }
        yawDegrees = 0f;
        return false;
    }

    public void Tick(double now)
    {
        if (_phase == ClientPhase.Finished)
        {
            return;
        }
        if (_phase == ClientPhase.Connecting)
        {
            CompleteConnect(now);
            return;
        }
        INetConnection connection = _connection!;
        try
        {
            while (_phase != ClientPhase.Finished && _phase != ClientPhase.Loading && connection.TryReceive(out INetMessage message))
            {
                _lastReceivedAt = now;
                try
                {
                    Handle(message);
                }
                catch (Exception exception) when (_phase == ClientPhase.Live && !(exception is ArgumentException) && !(exception is System.IO.InvalidDataException) && !(exception is InvalidOperationException))
                {
                    _log.LogError($"[Client] applying {message.Type} failed: {exception}");
                    Finish("An authoritative update failed; reconnect to reload the host world.");
                }
            }
        }
        catch (Exception exception) when (exception is ArgumentException || exception is System.IO.InvalidDataException || exception is InvalidOperationException)
        {
            Finish($"Desync or protocol error: {exception.Message}");
            return;
        }
        LogHealth(now, connection);
        if (_phase == ClientPhase.Live)
        {
            _mamas.Tick();
            _water.Tick(message => { SendCurrentPose(now); _connection!.Send(message); });
            Holds.ClientHolds.Tick(now);
            var toolBatches = WorldEdits.WorldEditRelay.TakeClientBatches();
            if (toolBatches.Count > 0) SendCurrentPose(now);
            foreach (ToolEditsMessage edits in toolBatches)
            {
                _connection!.Send(edits);
            }
        }
        if (!connection.IsOpen && _phase != ClientPhase.Finished)
        {
            Finish($"Disconnected: {connection.CloseReason}");
            return;
        }
        if (_phase == ClientPhase.WaitingToLoad)
        {
            TryStartLoad();
        }
        if (_phase == ClientPhase.Live)
        {
            TickLive(now);
        }
    }

    public void Dispose()
    {
        _connection?.Close("client left the session");
        if (_phase != ClientPhase.Finished)
        {
            Finish("Left the session");
        }
    }

    private void CompleteConnect(double now)
    {
        if (_retryAt >= 0)
        {
            if (now < _retryAt)
            {
                return;
            }
            _retryAt = -1;
            _connectAttempts++;
            _pendingConnect = _target.Start(ConnectTimeout);
            Status = $"Connecting to {_endpointDescription} (attempt {_connectAttempts}/{MaxConnectAttempts})...";
            return;
        }
        if (!_pendingConnect.IsCompleted)
        {
            return;
        }
        if (_pendingConnect.Connection == null)
        {
            string error = _pendingConnect.Error ?? $"Could not connect to {_endpointDescription}";
            if (_connectAttempts >= MaxConnectAttempts)
            {
                Finish($"{error} (gave up after {_connectAttempts} attempts)");
                return;
            }
            _log.LogInfo($"[Client] attempt {_connectAttempts} failed ({error}); retrying in {ConnectRetryDelaySeconds:0}s");
            _retryAt = now + ConnectRetryDelaySeconds;
            return;
        }
        _connection = _pendingConnect.Connection;
        _identityHello = new HelloMessage(ProtocolConstants.Version, FamilyTimeMultiplayer.Game.GameBuildIdentity.Current, _settings.PlayerName, _identity.PublicKey, Addons.SessionAddonRegistry.Descriptors);
        _connection.Send(_identityHello);
        _phase = ClientPhase.AwaitingWelcome;
        Status = $"Connected to {_endpointDescription}, waiting for the host's world...";
        _log.LogInfo($"[Client] connected to {_endpointDescription}");
    }

    private void Handle(INetMessage message)
    {
        switch (message)
        {
            case AddonMessage addon:
                if (_phase == ClientPhase.Live && addon.WorldGeneration == _welcome!.WorldGeneration) _addons!.Receive(PlayerRosterMessage.HostPlayerId, addon);
                break;
            case IdentityChallengeMessage challenge when _phase == ClientPhase.AwaitingWelcome && !_identityChallengeReceived:
                _identityChallengeReceived = true;
                _connection!.Send(new IdentityProofMessage(_identity.SignHandshake(challenge.Challenge, _identityHello!)));
                _connection = new SignedClientConnection(_connection, _identity, challenge.Challenge);
                Status = "Authenticating player identity...";
                break;
            case FamilyApprovalPendingMessage _ when _phase == ClientPhase.AwaitingWelcome || _phase == ClientPhase.Live:
                if (_phase == ClientPhase.Live)
                {
                    ClientCommandBridge.Detach();
                    Families.ClientNight.Detach();
                    Holds.ClientHolds.Detach();
                    _puppeteer.ReleaseAll();
                    _interpolator.Clear();
                    _phase = ClientPhase.AwaitingWelcome;
                }
                Status = $"Host must approve family restore. Identity {Core.Security.PlayerIdentity.Fingerprint(_identity.PublicKey)}";
                break;
            case RejectMessage reject:
                Finish($"Host refused: {reject.Reason}");
                break;
            case WelcomeMessage welcome:
                BeginWorldTransfer(welcome);
                break;
            case WorldChunkMessage chunk when _phase == ClientPhase.ReceivingWorld:
                _incomingWorld!.Append(chunk);
                Status = $"Downloading world {_incomingWorld.Progress:P0}";
                break;
            case WorldCompleteMessage complete when _phase == ClientPhase.ReceivingWorld:
                _incomingWorld!.Complete(complete);
                _incomingWorld = null;
                _phase = ClientPhase.WaitingToLoad;
                Status = "World downloaded, loading...";
                break;
            case PlayerRosterMessage roster:
                if (_phase == ClientPhase.Live && _addons != null)
                {
                    foreach (RosterEntry removed in _roster.Where(old => !roster.Players.Any(current => current.PlayerId == old.PlayerId))) _addons.PeerLeft(removed.PlayerId);
                    foreach (RosterEntry added in roster.Players.Where(current => !_roster.Any(old => old.PlayerId == current.PlayerId))) _addons.PeerReady(added.PlayerId);
                }
                _roster = roster.Players;
                break;
            case AgentSnapshotMessage snapshot when _phase == ClientPhase.Live:
                _addonHostClockOffset = snapshot.HostTimeSeconds - GameClock.Now;
                _interpolator.Push(FamilyTimeMultiplayer.Game.GameClock.Now, snapshot.HostTimeSeconds, ToPoses(snapshot));
                break;
            case RigidIndexMapMessage map when _phase == ClientPhase.ReceivingWorld || _phase == ClientPhase.WaitingToLoad:
                _pendingRigidMap = map;
                break;
            case RigidSnapshotMessage rigidSnapshot when _phase == ClientPhase.Live:
                _rigids.PushSnapshot(FamilyTimeMultiplayer.Game.GameClock.Now, rigidSnapshot);
                break;
            case RigidSpawnMessage rigidSpawn when _phase == ClientPhase.Live:
                _rigids.ApplySpawns(rigidSpawn);
                break;
            case RigidDespawnMessage rigidDespawn when _phase == ClientPhase.Live:
                _rigids.ApplyDespawns(rigidDespawn);
                break;
            case PropChangeMessage props when _phase == ClientPhase.Live:
                PropReplication.Apply(props);
                break;
            case CommandFeedbackMessage feedback when _phase == ClientPhase.Live:
                ClientCommandBridge.ApplyFeedback(feedback);
                break;
            case CommandOutlinesMessage outlines when _phase == ClientPhase.Live:
                Commands.ClientCommandOutlines.Apply(outlines, _rigids);
                break;
            case CommandMarkersMessage markers when _phase == ClientPhase.Live:
                Commands.ClientCommandMarkers.Apply(markers, _registry.Identities);
                break;
            case CommandAudioMessage audio when _phase == ClientPhase.Live:
                Commands.CommandAudioCatalog.Play(audio.Sound);
                break;
            case FamilyStatusMessage family when _phase == ClientPhase.Live:
                Families.LocalFamilyView.SetMembers(family.Members);
                Families.FamilyHud.Set(new Families.FamilyHudCounts(family.Children, family.Hungry, family.NotSleeping));
                break;
            case AgentInfoMessage info when _phase == ClientPhase.Live:
                AgentVisualsApplier.ApplyInfo(info, _registry.Identities, AvatarStableIndex);
                break;
            case AnimationEventsMessage animations when _phase == ClientPhase.Live:
                AgentVisualsApplier.ApplyAnimations(animations, _registry.Identities);
                break;
            case WaterTilesMessage water when _phase == ClientPhase.Live:
                _water.Apply(water);
                break;
            case WorldEditMessage edits when _phase == ClientPhase.Live:
                WorldEdits.WorldEditRelay.ApplyFromHost(edits, _registry.Identities);
                break;
            case HoldResultMessage holdResult when _phase == ClientPhase.Live:
                Holds.ClientHolds.ApplyResult(holdResult);
                break;
            case MamaStateMessage mamaState when _phase == ClientPhase.Live:
                Holds.ClientHolds.ApplyMamaState(mamaState);
                break;
            case MamaRosterMessage mamas when _phase == ClientPhase.Live:
                _mamas.Apply(mamas);
                break;
            case BedStatusMessage bed when _phase == ClientPhase.Live:
                Families.ClientNight.Apply(bed);
                break;
            case NightOutcomeMessage night when _phase == ClientPhase.Live:
                _log.LogInfo($"[Client] night: missing={night.ChildrenMissing} hungry={night.ChildrenHungry} grew by {night.GrownBy}");
                Families.ClientNight.Play(night, routine => (MultiplayerController.Current ?? throw new System.InvalidOperationException("No MultiplayerController to run the night on.")).Run(routine));
                break;
            case AgentSpawnMessage spawn when _phase == ClientPhase.Live:
                _registry.ApplySpawns(spawn);
                break;
            case AgentDespawnMessage despawn when _phase == ClientPhase.Live:
                _registry.ApplyDespawns(despawn, AvatarStableIndex);
                break;
            case DayStateMessage day when _phase == ClientPhase.Live:
                _day.Apply(day);
                break;
            default:
                throw new System.IO.InvalidDataException($"Unexpected {message.Type} while {_phase}.");
        }
    }

    private void BeginWorldTransfer(WelcomeMessage welcome)
    {
        ProtocolConstants.RequireWorldLength(welcome.WorldByteLength);
        if (!_identityChallengeReceived) throw new System.IO.InvalidDataException("World transfer arrived before player authentication.");
        if (_phase == ClientPhase.Connecting || _phase == ClientPhase.Finished) throw new System.IO.InvalidDataException("World transfer arrived outside a connected session.");
        _worldLoadGeneration++;
        if (_phase == ClientPhase.Live)
        {
            _log.LogInfo("[Client] host is re-sending the world (host loaded a different save)");
            _puppeteer.ReleaseAll();
            _interpolator.Clear();
        }
        _incomingWorld?.Dispose();
        _pendingRigidMap = null;
        ClientCommandBridge.Detach();
        Families.ClientNight.Detach();
        Holds.ClientHolds.Detach();
        Families.LocalFamilyView.Clear();
        Families.FamilyHud.Clear();
        _mamas.Clear();
        _rigids.ReleaseAll();
        _welcome = welcome;
        _incomingWorld = new IncomingWorldFile(WorldFiles.PathFor(WorldFiles.ClientReceivedMapName), welcome.WorldByteLength);
        _phase = ClientPhase.ReceivingWorld;
        Status = "Downloading world 0%";
        _log.LogInfo($"[Client] welcome as player #{welcome.PlayerId}, avatar {welcome.AvatarStableIndex}, transfer {welcome.TransferId}, world {welcome.WorldByteLength / (1024 * 1024)} MB");
    }

    private void TryStartLoad()
    {
        WorldSerializer? serializer = World.worldSerializer;
        if (serializer == null || serializer.IsBusy || _pendingRigidMap == null)
        {
            return;
        }
        _phase = ClientPhase.Loading;
        int generation = _worldLoadGeneration;
        serializer.Load(WorldFiles.ClientReceivedMapName, success => { if (generation == _worldLoadGeneration) OnWorldLoaded(success); });
    }

    private void OnWorldLoaded(bool success)
    {
        if (_phase != ClientPhase.Loading)
        {
            return;
        }
        if (!success)
        {
            Finish("The host's world failed to load on this machine; see the game log.");
            return;
        }
        int adopted = _registry.AdoptLoadedWorld();
        _rigids.Bind(_pendingRigidMap!);
        LocalPlayer.Possess(GatInterop.ToHandle(AvatarStableIndex));
        ReplicationGate.EnterClient();
        WeatherStateBridge.SuppressClientLoops();
        _connection!.Send(new ClientReadyMessage(_welcome!.TransferId));
        _phase = ClientPhase.Live;
        if (_addons == null) _addons = Addons.SessionAddonRegistry.Start(false, () => LocalPlayerId, () => _welcome!.WorldGeneration, SendAddon, () => GameClock.Now + _addonHostClockOffset);
        else _addons.Reload();
        foreach (RosterEntry player in _roster) _addons.PeerReady(player.PlayerId);
        ClientCommandBridge.Attach(this);
        Families.ClientNight.Attach(this);
        Holds.ClientHolds.UpdateInterval = _settings.PoseInterval;
        Holds.ClientHolds.Attach(this);
        Status = $"Playing on {_endpointDescription}";
        _water.Reset();
        _poseInitialized = false;
        _addonPoseBound = false;
        _pose.Reset();
        _log.LogInfo($"[Client] world loaded, adopted {adopted} agents and {_rigids.Count} items, possessing avatar {AvatarStableIndex}");
    }

    private void TickLive(double now)
    {
        _interpolator.Sample(FamilyTimeMultiplayer.Game.GameClock.Now, _sampled);
        _puppeteer.Apply(_sampled, _registry.Identities, AvatarStableIndex);
        _rigids.Apply(FamilyTimeMultiplayer.Game.GameClock.Now);
        if (CanSendFootPose() && now >= _nextPoseTime && LocalPlayer.IsWorldReady)
        {
            if (!_poseInitialized)
            {
                Matrix4x4? matrix = World.gat.GetInstanceMatrixImmediate(LocalPlayer.PossessedHandle);
                if (matrix == null || Vector3.Distance(matrix.Value.GetColumn(3), LocalPlayer.Position) > 1) return;
                _poseInitialized = true;
            }
            _nextPoseTime = now + _settings.PoseInterval;
            _connection!.Send(_pose.Sample(now));
        }
        else if (_addonPoseBound && now >= _nextPoseTime) SendAddonLook(now);
    }

    private void SendCurrentPose(double now)
    {
        if (!CanSendFootPose()) { SendAddonLook(now); return; }
        if (!_poseInitialized) return;
        _connection!.Send(_pose.Sample(now));
        _nextPoseTime = now + _settings.PoseInterval;
    }

    private void SendAddonLook(double now)
    {
        if (_addons == null || !_addons.TryPose(LocalPlayerId, out ClientPoseMessage canonical) || !LocalPlayer.IsWorldReady) return;
        var view = new ClientPoseMessage(canonical.Position, LocalPlayer.YawDegrees, LocalPlayer.PitchDegrees, canonical.Velocity);
        _connection!.Send(Core.Safety.AddonCommandPose.Compose(canonical, view));
        _nextPoseTime = now + _settings.PoseInterval;
    }

    private bool CanSendFootPose()
    {
        bool bound = _addons?.TryPose(LocalPlayerId, out _) == true;
        if (bound != _addonPoseBound)
        {
            _addonPoseBound = bound;
            _poseInitialized = false;
            _pose.Reset();
        }
        return !bound;
    }

    public void SendIntent(CommandIntentMessage intent)
    {
        if (_phase == ClientPhase.Live)
        {
            SendCurrentPose(FamilyTimeMultiplayer.Game.GameClock.Now);
            _connection!.Send(intent);
            _log.LogInfo($"[Client] command {intent.Kind}/{intent.TargetKind} sent to host");
        }
    }

    private double _lastReceivedAt = FamilyTimeMultiplayer.Game.GameClock.Now;
    private double _nextHealth;

    private void LogHealth(double now, INetConnection connection)
    {
        if (now < _nextHealth)
        {
            return;
        }
        _nextHealth = now + 10.0;
        WorldSerializer? serializer = World.worldSerializer;
        _log.LogInfo($"[Health] client phase={_phase} open={connection.IsOpen} queued={connection.PendingSendFrames} lastRx={now - _lastReceivedAt:0.0}s fps={1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f):0} timeScale={FamilyTimeMultiplayer.Game.GameApiCompatibility.TimeScale:0.##} worldBusy={serializer?.IsBusy} loading={serializer?.isLoadingInProgress} puppets={_puppeteer.LastAppliedCount} unmapped={_puppeteer.LastUnmappedCount}");
    }

    public void SendHold(HoldMessage message)
    {
        if (_phase == ClientPhase.Live)
        {
            if (message.Phase != HoldPhase.Update) SendCurrentPose(FamilyTimeMultiplayer.Game.GameClock.Now);
            _connection!.Send(message);
            if (message.Phase != HoldPhase.Update)
            {
                _log.LogInfo($"[Hold] {message.Phase} {message.Target} at ({message.Position.X:0.0}, {message.Position.Y:0.0}, {message.Position.Z:0.0})");
            }
        }
    }

    public void SendMamaToggle(ushort agent)
    {
        if (_phase == ClientPhase.Live)
        {
            _connection!.Send(new MamaToggleMessage(agent));
        }
    }

    public void SetRigidLocallyHeld(int netId, bool held) => _rigids.SetLocallyHeld(netId, held);

    public bool TryGetHostAgent(ushort localStableIndex, out ushort hostStableIndex) =>
        _registry.Identities.TryGetHost(localStableIndex, out hostStableIndex);

    public void SetAgentLocallyHeld(ushort localStableIndex, bool held)
    {
        if (held)
        {
            _puppeteer.HeldLocally.Add(localStableIndex);
        }
        else
        {
            _puppeteer.HeldLocally.Remove(localStableIndex);
        }
    }

    public void SendBedRequest(bool ready)
    {
        if (_phase == ClientPhase.Live)
        {
            _connection!.Send(new BedRequestMessage(ready));
        }
    }

    public bool TryGetRigidNetId(RigidTransform rigid, out int netId) => _rigids.TryGetNetId(rigid, out netId);

    private static IEnumerable<KeyValuePair<ushort, NetPose>> ToPoses(AgentSnapshotMessage snapshot)
    {
        foreach (AgentState agent in snapshot.Agents)
        {
            yield return new KeyValuePair<ushort, NetPose>(agent.StableIndex, agent.Pose);
        }
    }


    private void SendAddon(int? recipient, AddonMessage message)
    {
        if (_phase != ClientPhase.Live || recipient != PlayerRosterMessage.HostPlayerId) throw new InvalidOperationException("Client addon sends require a live host connection.");
        SendCurrentPose(GameClock.Now);
        _connection!.Send(message);
    }

    private void Finish(string reason)
    {
        _addons?.Dispose();
        _addons = null;
        _phase = ClientPhase.Finished;
        _water.Dispose();
        _incomingWorld?.Dispose();
        _incomingWorld = null;
        _puppeteer.ReleaseAll();
        _rigids.ReleaseAll();
        ClientCommandBridge.Detach();
        Families.ClientNight.Detach();
        Holds.ClientHolds.Detach();
        Families.LocalFamilyView.Clear();
        Families.FamilyHud.Clear();
        _mamas.Clear();
        ReplicationGate.Leave();
        _connection?.Close(reason);
        _identity.Dispose();
        Status = reason;
        _log.LogWarning($"[Client] session ended: {reason}");
    }
}
