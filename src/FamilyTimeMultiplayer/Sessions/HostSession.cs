using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Actions;
using FamilyTimeMultiplayer.Core.Compatibility;
using FamilyTimeMultiplayer.Addons;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Replication;
using FamilyTimeMultiplayer.Core.Safety;
using FamilyTimeMultiplayer.Core.Transport;
using FamilyTimeMultiplayer.Core.Transport.Relay;
using FamilyTimeMultiplayer.Families;
using FamilyTimeMultiplayer.Game;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.Sessions;

public sealed partial class HostSession : ISession, IHostReplicationObserver, IHostCommandLockObserver, INightParticipants, IAddonReplicationEndpoint
{
    private const int MaxQueuedFramesBeforeSkippingSnapshots = 8;
    private const double DayStateInterval = 1.0;
    private const double PoolBroadcastInterval = 1.0;
    private const double FamilyStatusInterval = 1.0;

    private enum PeerPhase
    {
        AwaitingHello,
        AwaitingIdentityProof,
        AwaitingFamilyApproval,
        AwaitingProvision,
        Provisioning,
        AwaitingReady,
        Live,
    }

    private sealed class Peer
    {
        public Peer(INetConnection connection, int playerId)
        {
            Connection = connection;
            PlayerId = playerId;
        }

        public INetConnection Connection { get; }
        public int PlayerId { get; }
        public long TransferId { get; set; }
        public double ConnectedAt { get; } = FamilyTimeMultiplayer.Game.GameClock.Now;
        public string Name { get; set; } = "?";
        public string RequestedName { get; set; } = string.Empty;
        public string PublicKey { get; set; } = string.Empty;
        public HelloMessage? IdentityHello { get; set; }
        public byte[]? IdentityChallenge { get; set; }
        public bool Authenticated { get; set; }
        public IReadOnlyList<AddonDescriptor> SharedAddons { get; set; } = Array.Empty<AddonDescriptor>();
        public Core.Security.SignedClientVerifier? ClientVerifier { get; set; }
        public string Owner { get; set; } = string.Empty;
        public PeerPhase Phase { get; set; } = PeerPhase.AwaitingHello;
        public Handle Avatar { get; set; } = Handle.Null;
        public List<INetMessage>? Backlog { get; set; }
        public int BacklogBytes { get; set; }
        public Vector3? LastPosition { get; set; }
        public float LastYawDegrees { get; set; }
        public (int Available, int Total)? LastPool { get; set; }
        public FamilyHudCounts? LastFamilyStatus { get; set; }
        public ushort[]? LastFamilyMembers { get; set; }
        public byte[]? LastCommandMarkers { get; set; }
        public byte[]? LastCommandOutlines { get; set; }
        public Core.Safety.WorkBudget Work { get; } = new Core.Safety.WorkBudget();
        public WaterViewMessage? WaterView { get; set; }
        public Core.Water.WaterTileTracker WaterTiles { get; } = new Core.Water.WaterTileTracker();
        public int WaterGeneration { get; set; }
        public double NextWater { get; set; }
        public double WaterRefreshAt { get; set; }
        public bool StarterFamilyHandled { get; set; }
        public double LastReceivedAt { get; set; } = FamilyTimeMultiplayer.Game.GameClock.Now;
    }

    private readonly ManualLogSource _log;
    private readonly MultiplayerSettings _settings;
    private readonly ICoroutineHost _coroutines;
    private readonly TcpNetListener _listener;
    private readonly RelayHostListener? _relay;
    private RelayHostState? _lastRelayState;
    private readonly AgentSnapshotSampler _sampler = new AgentSnapshotSampler();
    private readonly RemoteAvatarService _avatars = new RemoteAvatarService();
    private readonly DayStateBridge _day = new DayStateBridge();
    private readonly ReplicationCapabilities _replicationCapabilities = new(SessionAddonRegistry.Descriptors);
    private readonly GenericEntityRegistry<object> _entityRegistry = new GenericEntityRegistry<object>(1);
    private readonly GenericStateReplicationService<object> _genericStates;
    private readonly GenericActionBridge<object> _genericActions;
    private readonly GenericActionSessionEndpoint<object> _genericActionEndpoint;
    private readonly LegacyAgentIdentityAdapter _agentEntities;
    private readonly HostRigidRegistry _rigids;
    private readonly HostCommandHub _commands;
    private double _nextPoolBroadcast;
    private double _nextFamilyStatus;
    private double _nextCommandMarkers;
    private HostFamilyService? Families => _familyMode == Core.Protocol.FamilyMode.Separate ? HostFamilyService.Current : null;
    private HostMamaService? Mamas => Families?.Mamas;
    private readonly HostNightCoordinator _night;
    private readonly Holds.HostHoldRegistry _holds;
    private readonly AgentInfoComponentAdapter _agentInfoState = new((_, _) => throw new InvalidOperationException("Host cannot apply remote agent info."));
    private readonly AgentInfoSampler _info = new AgentInfoSampler();
    private readonly HostWaterSampler _water;
    private int _waterRotation;
    private readonly WeatherStateBridge _weather = new WeatherStateBridge();
    private string _lastMamaState = string.Empty;
    private double _nextMamaState;
    private readonly List<Peer> _peers = new List<Peer>();
    private readonly string _hostName;
    private int _nextPlayerId = PlayerRosterMessage.HostPlayerId + 1;
    private bool _provisioningActive;
    private double _nextSnapshotTime;
    private double _nextDayStateTime;
    private bool _rosterDirty;
    private IReadOnlyList<RosterEntry> _roster = Array.Empty<RosterEntry>();

    private readonly FamilyMode _familyMode;
    private readonly Addons.SessionAddonRuntime _addons;
    private long _addonWorldGeneration = 1;

    public HostSession(ManualLogSource log, MultiplayerSettings settings, ICoroutineHost coroutines, FamilyMode familyMode)
    {
        _agentEntities = new LegacyAgentIdentityAdapter(_entityRegistry);
        _genericStates = new GenericStateReplicationService<object>(_entityRegistry, isHost: true);
        _genericStates.RegisterAdapter(_agentInfoState);
        _genericStates.RegisterAdapter(new CommandLockComponentAdapter(NativeCommandLockReplication.Read,
            (_, _) => throw new InvalidOperationException("Host cannot apply remote command locks.")));
        _rigids = new HostRigidRegistry(_entityRegistry);
        _familyMode = familyMode;
        _log = log;
        _water = new HostWaterSampler(log);
        _settings = settings;
        _coroutines = coroutines;
        _hostName = settings.PlayerName;
        if (familyMode == Core.Protocol.FamilyMode.Shared && HostFamilyService.Current != null)
        {
            throw new InvalidOperationException("This world has separate families; host it with \"Host separate\".");
        }
        try
        {
            _listener = new TcpNetListener(IPAddress.Any, settings.PortEntry.Value);
            if (!string.IsNullOrWhiteSpace(settings.RelayAddress))
            {
                (string relayHost, int? relayPort) = Core.Launch.LaunchOptions.ParseEndpoint(settings.RelayAddress);
                _relay = new RelayHostListener(relayHost, relayPort ?? RelayWire.DefaultPort);
                _log.LogInfo($"[Host] opening a room on relay {_relay.Endpoint}");
            }
            _rigids.AdoptCurrentWorld();
            AdoptCurrentAgents();
            _commands = new HostCommandHub(log);
            _genericActions = new GenericActionBridge<object>(_entityRegistry, (playerId, callback) => _commands.RunAsPlayer(playerId, callback));
            _genericActionEndpoint = new GenericActionSessionEndpoint<object>(_genericActions);
            _genericActions.Register(new NativeCommandActionAdapter(ExecuteNativeCommand, AuthorizeNativeCommand));
            if (familyMode == Core.Protocol.FamilyMode.Separate)
            {
                HostFamilyService.EnsureStarted(log, _hostName);
            }
            _holds = new Holds.HostHoldRegistry(log, _rigids);
            _night = new HostNightCoordinator(log, this, PlayerRosterMessage.HostPlayerId, _hostName);
            ReplicationGate.EnterHost(this);
            _rosterDirty = true;
            _addons = Addons.SessionAddonRegistry.Start(true, () => LocalPlayerId, () => _addonWorldGeneration, SendAddon, this);
            Status = $"Hosting on port {_listener.Port} ({familyMode} family)";
            _log.LogInfo($"[Host] listening on 0.0.0.0:{_listener.Port}");
        }
        catch
        {
            DisposeFailedStart();
            throw;
        }
    }

    private void DisposeFailedStart()
    {
        IDisposable?[] resources = { _addons, _night, _holds, _commands, _info, _water, _listener, _relay };
        foreach (IDisposable? resource in resources)
        {
            try { resource?.Dispose(); }
            catch (Exception exception) { _log.LogError($"[Host] failed startup cleanup: {exception}"); }
        }
        HostPlayerAuthority.Clear();
        ReplicationGate.Leave();
    }

    public SessionRole Role => SessionRole.Host;

    public string Status { get; private set; }

    public bool IsFinished => false;

    public IReadOnlyList<RosterEntry> Roster => _roster;

    public int LocalPlayerId => PlayerRosterMessage.HostPlayerId;

    public FamilyMode? FamilyMode => _familyMode;

    public bool TryGetRemotePlayerPosition(RosterEntry player, out Vector3 position)
    {
        Peer? peer = _peers.FirstOrDefault(p => p.PlayerId == player.PlayerId);
        position = peer?.LastPosition ?? Vector3.zero;
        return peer?.LastPosition != null;
    }

    public bool TryGetRemotePlayerYaw(RosterEntry player, out float yawDegrees)
    {
        Peer? peer = _peers.FirstOrDefault(p => p.PlayerId == player.PlayerId);
        yawDegrees = peer?.LastYawDegrees ?? 0f;
        return peer?.LastPosition != null;
    }

    public void Tick(double now)
    {
        if (World.worldSerializer?.isLoadingInProgress == true) return;
        UpdateAddonPoses();
        AcceptConnections();
        _drainTimer.Restart();
        Peer[] draining = _peers.ToArray();
        for (int offset = 0; offset < draining.Length && _drainTimer.ElapsedMilliseconds < 4; offset++)
        {
            Peer peer = draining[(offset + _drainRotation) % draining.Length];
            if (!peer.Authenticated && now - peer.ConnectedAt > 15) peer.Connection.Close("Hello handshake timed out.");
            try
            {
                DrainMessages(peer);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidDataException)
            {
                _log.LogWarning($"[Host] '{peer.Name}' sent invalid data, disconnecting: {exception.Message}");
                peer.Connection.Close($"invalid data: {exception.Message}");
            }
        }
        _drainRotation = draining.Length == 0 ? 0 : (_drainRotation + 1) % draining.Length;
        DropClosedPeers();
        StartNextProvisioning();
        PublishRosterIfDirty();
        SuspendAddonRestrictedGestures();
        _commands.Tick();
        StreamCommandMarkers(now);
        foreach ((int playerId, CommandAudioMessage audio) in _commands.TakeAudio())
        {
            Peer? recipient = _peers.FirstOrDefault(peer => peer.PlayerId == playerId && peer.Phase == PeerPhase.Live);
            recipient?.Connection.Send(audio);
        }
        TickFamilies(now);
        _night.Tick();
        _holds.Tick(now);
        BroadcastMamaState(now);
        _weather.Tick(now);
        StreamWater(now);
        IReadOnlyList<int> destroyedRigids = _rigids.CollectDestroyed();
        if (destroyedRigids.Count > 0)
        {
            foreach (int netId in destroyedRigids) ForgetGenericEntityState(LegacyRigidIdentityAdapter.Kind, (ulong)netId);
            SendWorldEvent(new RigidDespawnMessage(destroyedRigids));
        }
        foreach (WorldEditMessage edits in WorldEdits.WorldEditRelay.TakeHostBatches())
        {
            SendWorldEvent(edits);
        }
        foreach (AnimationEventsMessage animations in AnimationRelay.TakeBatches())
        {
            SendToLive(animations);
        }
        if (_peers.Any(p => p.Phase == PeerPhase.Live))
        {
            _info.Tick(now, PublishAgentInfo);
        }
        StreamCommandLocks(now);
        StreamSnapshots(now);
        StreamDayState(now);
        BroadcastPool(now);
        LogHealth(now);
        int live = _peers.Count(p => p.Phase == PeerPhase.Live);
        Status = $"Hosting on port {_listener.Port}{DescribeRelay()} - {live} live / {_peers.Count} connected";
    }

    public void OnAgentsCreated(IReadOnlyList<Handle> handles, IReadOnlyList<int> templateIndices, IReadOnlyList<Matrix4x4> matrices)
    {
        foreach (Handle handle in handles)
        {
            if (!handle.isNull) _agentEntities.Bind(handle.stableIndex, handle.stableIndex);
        }
        if (!_peers.Any(ReceivesWorldEvents))
        {
            return;
        }
        var spawns = new AgentSpawn[handles.Count];
        for (int i = 0; i < handles.Count; i++)
        {
            spawns[i] = new AgentSpawn(handles[i].stableIndex, templateIndices[i], GatInterop.ToNetPose(matrices[i]));
        }
        SendWorldEvent(new AgentSpawnMessage(spawns));
    }

    public void OnAgentsDeleted(IReadOnlyList<Handle> handles)
    {
        foreach (Handle handle in handles)
        {
            if (!handle.isNull)
            {
                _agentInfoState.Forget(handle.stableIndex);
                _lastCommandLocks.Remove(handle.stableIndex);
                _agentEntities.Unbind(handle.stableIndex);
                ForgetGenericEntityState(LegacyAgentIdentityAdapter.Kind, handle.stableIndex);
            }
        }
        if (!_peers.Any(ReceivesWorldEvents))
        {
            return;
        }
        SendWorldEvent(new AgentDespawnMessage(handles.Where(h => !h.isNull).Select(h => h.stableIndex).ToArray()));
    }

    public void OnRigidSpawned(RigidTransform rigid)
    {
        RigidSpawn spawn = _rigids.Register(rigid);
        SendWorldEvent(new RigidSpawnMessage(new[] { spawn }));
    }

    public void OnRigidDeleting(RigidTransform rigid)
    {
        if (_rigids.TryUnregister(rigid, out int netId))
        {
            ForgetGenericEntityState(LegacyRigidIdentityAdapter.Kind, (ulong)netId);
            SendWorldEvent(new RigidDespawnMessage(new[] { netId }));
        }
    }

    public void OnPropAdded(uint cellX, uint cellZ, EnvironmentMesh prop) =>
        SendWorldEvent(new PropChangeMessage(new[] { new PropChange(cellX, cellZ, added: true, prop.name) }));

    public void OnPropRemoved(uint cellX, uint cellZ) =>
        SendWorldEvent(new PropChangeMessage(new[] { new PropChange(cellX, cellZ, added: false, string.Empty) }));

    public void OnHostWorldReloaded()
    {
        _addonWorldGeneration++;
        _entityRegistry.ChangeWorldGeneration(_addonWorldGeneration);
        _agentInfoState.Reset();
        _lastCommandLocks.Clear();
        _nextCommandLockSample = 0;
        _info.SendEverythingNext();
        _addons.Reload();
        HostPlayerAuthority.Remove(LocalPlayerId);
        _water.Reset();
        _rigids.AdoptCurrentWorld();
        AdoptCurrentAgents();
        if (_familyMode == Core.Protocol.FamilyMode.Separate)
        {
            HostFamilyService.EnsureStarted(_log, _hostName);
        }
        _log.LogInfo("[Host] host loaded a different world; re-sending it to every connected player");
        foreach (Peer peer in _peers.Where(p => p.Authenticated))
        {
            _addons.PeerLeft(peer.PlayerId);
            _commands.RemovePlayer(peer.PlayerId);
            HostPlayerAuthority.Remove(peer.PlayerId);
            peer.TransferId++;
            peer.Avatar = Handle.Null;
            peer.Backlog = null;
            peer.StarterFamilyHandled = false;
            peer.Owner = string.Empty;
            peer.Phase = PeerPhase.AwaitingProvision;
        }
        _rosterDirty = true;
    }

    public void Dispose()
    {
        _addons.Dispose();
        _commands.Dispose();
        foreach (Peer peer in _peers)
        {
            peer.ClientVerifier?.Dispose();
            peer.Connection.Close("host stopped the session");
            _holds.ReleaseAll(peer.PlayerId);
            Families?.UnregisterPlayer(peer.PlayerId);
            _avatars.Despawn(peer.Avatar);
        }
        _peers.Clear();
        HostPlayerAuthority.Clear();
        _night.Dispose();
        _holds.Dispose();
        _info.Dispose();
        _water.Dispose();
        AnimationRelay.Reset();
        WorldEdits.WorldEditRelay.Reset();
        _listener.Dispose();
        _relay?.Dispose();
        ReplicationGate.Leave();
        _log.LogInfo("[Host] session stopped");
    }

    private static bool ReceivesWorldEvents(Peer peer) =>
        peer.Phase == PeerPhase.Live || peer.Backlog != null;

    private void AdoptCurrentAgents()
    {
        if (World.gat == null) return;
        for (int physicalIndex = 0; physicalIndex < World.gat.instanceCount; physicalIndex++)
        {
            Handle handle = World.gat.GetHandle((ushort)physicalIndex);
            if (!handle.isNull) _agentEntities.Bind(handle.stableIndex, handle.stableIndex);
        }
    }

    private void SendWorldEvent(INetMessage message)
    {
        foreach (Peer peer in _peers)
        {
            if (message is GenericEntityStateMessage state && !_replicationCapabilities.SupportsState(peer.SharedAddons, state)) continue;
            if (peer.Backlog != null)
            {
                peer.BacklogBytes += MessageCodec.Encode(message).Length;
                if (peer.Backlog.Count >= 4096 || peer.BacklogBytes > 16 * 1024 * 1024) { peer.Connection.Close("world provision backlog exceeded"); peer.Backlog = null; continue; }
                peer.Backlog.Add(message);
            }
            else if (peer.Phase == PeerPhase.Live)
            {
                peer.Connection.Send(message);
            }
        }
    }

    public string? RoomCode => _relay?.State == RelayHostState.Ready ? _relay.Code : null;

    private string DescribeRelay()
    {
        if (_relay == null)
        {
            return string.Empty;
        }
        if (_relay.State != _lastRelayState)
        {
            _lastRelayState = _relay.State;
            if (_relay.State == RelayHostState.Ready)
            {
                _log.LogInfo($"[Host] relay room {_relay.Code} is open on {_relay.Endpoint}");
            }
            else if (_relay.State == RelayHostState.Failed)
            {
                _log.LogError($"[Host] {_relay.Error}");
            }
        }
        return _relay.State switch
        {
            RelayHostState.Ready => $", room {_relay.Code}",
            RelayHostState.Connecting => ", opening relay room...",
            _ => $", relay failed: {_relay.Error}",
        };
    }

    private void AcceptConnections()
    {
        AcceptFrom(_listener);
        if (_relay != null)
        {
            AcceptFrom(_relay);
        }
    }

    private void AcceptFrom(INetListener listener)
    {
        for (int count = 0; count < 4 && listener.TryAccept(out INetConnection connection); count++)
        {
            if (_peers.Count + 1 >= _settings.MaxPlayersEntry.Value)
            {
                connection.Send(new RejectMessage($"Session is full ({_settings.MaxPlayersEntry.Value} players)."));
                connection.Close("session full");
                continue;
            }
            var peer = new Peer(connection, _nextPlayerId++);
            _peers.Add(peer);
            _log.LogInfo($"[Host] connection #{peer.PlayerId} from {connection.RemoteDescription}");
        }
    }

    private int _drainRotation;
    private readonly System.Diagnostics.Stopwatch _drainTimer = new System.Diagnostics.Stopwatch();

    private void DrainMessages(Peer peer)
    {
        int remaining = 32;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (remaining-- > 0 && timer.ElapsedMilliseconds < 2 && _drainTimer.ElapsedMilliseconds < 4 && peer.Connection.IsOpen && peer.Connection.TryReceive(out INetMessage message))
        {
            peer.LastReceivedAt = FamilyTimeMultiplayer.Game.GameClock.Now;
            if (peer.Authenticated)
            {
                if (message is not SignedClientMessage signed) throw new InvalidDataException("Authenticated client packets must be signed.");
                message = peer.ClientVerifier!.Decode(signed);
            }
            int cost = message is AddonMessage addon ? 256 + addon.Payload.Length / 64 : message is ToolEditsMessage tool ? 64 + tool.Edits.Edits.Count * 64 : message is WorldEditMessage world ? 64 + world.Edits.Count * 16 : message is GenericActionRequestMessage generic ? 256 + generic.Payload.Length / 64 : message is CommandIntentMessage ? 256 : 8;
            if (!peer.Work.TrySpend(cost, peer.LastReceivedAt)) throw new InvalidDataException("Peer work rate limit exceeded.");
            try
            {
                HandlePeerMessage(peer, message);
            }
            catch (Exception exception) when (!(exception is ArgumentException) && !(exception is InvalidDataException))
            {
                InvalidateReplicas(exception);
                return;
            }
        }
    }

    private void StreamWater(double now)
    {
        Peer[] live = _peers.Where(peer => peer.Phase == PeerPhase.Live && peer.Connection.IsOpen).ToArray();
        for (int i = 0; i < live.Length; i++)
        {
            int index = (i + _waterRotation) % live.Length;
            Peer peer = live[index];
            if (now < peer.NextWater || peer.Connection.PendingSendFrames > 32) continue;
            ClientPoseMessage position = HostPlayerAuthority.RequirePose(peer.PlayerId);
            WaterViewMessage view = peer.WaterView ?? new WaterViewMessage(Mathf.FloorToInt(position.Position.X / 32) * 32 - 128, Mathf.FloorToInt(position.Position.Z / 32) * 32 - 128);
            int generation = peer.WaterGeneration;
            if (!_water.Request(view, levels =>
            {
                if (peer.Phase != PeerPhase.Live || !peer.Connection.IsOpen || generation != peer.WaterGeneration) return;
                if (now >= peer.WaterRefreshAt) { peer.WaterTiles.Reset(); peer.WaterRefreshAt = now + 5; }
                WaterTilesMessage changes = peer.WaterTiles.Changes(view, levels);
                if (changes.Tiles.Count > 0) peer.Connection.Send(changes);
            })) return;
            peer.NextWater = now + 0.5;
            _waterRotation = (index + 1) % live.Length;
            return;
        }
    }

    private void InvalidateReplicas(Exception exception)
    {
        _log.LogError($"[Host] native mutation failed; clients must reload the authoritative world: {exception}");
        foreach (Peer peer in _peers) peer.Connection.Close("A world action failed; reconnect to reload the host world.");
        UI.PlayerNotice.Show("A world action failed. Clients need to reconnect (details in the log).");
    }


    private void SendAddon(int? recipient, AddonMessage message)
    {
        if (recipient == LocalPlayerId) throw new InvalidOperationException("Host addon messages must target remote players.");
        foreach (Peer peer in _peers.Where(peer => peer.Phase == PeerPhase.Live && peer.Connection.IsOpen && (recipient == null || recipient == peer.PlayerId) && peer.SharedAddons.Any(addon => addon.Id == message.AddonId))) peer.Connection.Send(message);
    }

    private void SuspendAddonRestrictedGestures()
    {
        CommandIntentMessage startFollow = CommandIntentMessage.Follow(true);
        foreach (int playerId in _peers.Where(peer => peer.Phase == PeerPhase.Live).Select(peer => peer.PlayerId).Append(LocalPlayerId))
            if (!_addons.AllowsNativeAction(playerId, startFollow)) _commands.SuspendInputGestures(playerId);
    }

    private void UpdateAddonPoses()
    {
        if (!HostPlayerAuthority.Exists(LocalPlayerId)) HostPlayerAuthority.Register(LocalPlayerId, LocalPlayer.PossessedHandle);
        HostPlayerAuthority.AcceptAuthoritativePose(LocalPlayerId, new ClientPoseMessage(GatInterop.ToNet(LocalPlayer.Position), LocalPlayer.YawDegrees, LocalPlayer.PitchDegrees, NetVector3.Zero));
        foreach (int playerId in _peers.Where(peer => peer.Phase == PeerPhase.Live).Select(peer => peer.PlayerId).Append(LocalPlayerId))
        {
            Peer? peer = _peers.FirstOrDefault(candidate => candidate.PlayerId == playerId);
            if (!_addons.TryPose(playerId, out ClientPoseMessage pose))
            {
                HostPlayerAuthority.ReleaseAddonPose(playerId);
                continue;
            }
            HostPlayerAuthority.AcceptAddonPose(playerId, pose);
            if (peer == null) continue;
            _avatars.ApplyPose(peer.Avatar, pose);
            _commands.UpdatePose(playerId, pose);
            peer.LastPosition = GatInterop.ToUnity(pose.Position); peer.LastYawDegrees = pose.YawDegrees;
        }
    }

    private string? AuthorizeNativeCommand(int playerId, CommandIntentMessage intent)
    {
        if (!_addons.AllowsNativeAction(playerId, intent)) return "Current addon state does not permit this action.";
        try { _commands.ValidateIntent(playerId, intent, _rigids); return null; }
        catch (InvalidDataException exception) { return exception.Message; }
    }

    private void ExecuteNativeCommand(int playerId, CommandIntentMessage intent)
    {
        Peer peer = _peers.Single(candidate => candidate.PlayerId == playerId);
        _commands.Handle(playerId, intent, _rigids, result => SendCommandFeedback(peer, result));
    }

    private void SendCommandFeedback(Peer peer, CommandResult result)
    {
        if (!peer.Connection.IsOpen || peer.Phase != PeerPhase.Live) return;
        (int available, int total) = PoolFor(peer);
        peer.LastPool = (available, total);
        peer.Connection.Send(new CommandFeedbackMessage(available, total, result.Outcome, result.Detail));
    }

    private void HandlePeerMessage(Peer peer, INetMessage message)
    {
        if (peer.TransferId > 1 && peer.Phase != PeerPhase.Live && message is WaterViewMessage or CommandIntentMessage or HoldMessage or ToolEditsMessage or MamaToggleMessage or BedRequestMessage) return;
        if (peer.Phase == PeerPhase.Live && !(message is GenericActionRequestMessage native && native.ActionId == NativeCommandActionAdapter.ActionId) && !_addons.AllowsNativeAction(peer.PlayerId, message))
        {
            RefuseAddonRestrictedAction(peer, message);
            return;
        }
        {
            switch (message)
            {
                case HelloMessage hello when peer.Phase == PeerPhase.AwaitingHello:
                    HandleHello(peer, hello);
                    break;
                case IdentityProofMessage proof when peer.Phase == PeerPhase.AwaitingIdentityProof:
                    HandleIdentityProof(peer, proof);
                    break;
                case AddonMessage addon when peer.Phase == PeerPhase.Live:
                    if (!peer.SharedAddons.Any(capability => capability.Id == addon.AddonId)) throw new InvalidDataException("Addon was not negotiated for this peer.");
                    if (addon.WorldGeneration == _addonWorldGeneration) _addons.Receive(peer.PlayerId, addon);
                    break;
                case AddonMessage _:
                    break;
                case GenericEntityStateBatchMessage:
                case GenericEntityStateMessage:
                    Reject(peer, "Only the host may publish authoritative component state.");
                    break;
                case GenericActionRequestMessage action when peer.Phase == PeerPhase.Live:
                    if (!_replicationCapabilities.SupportsAction(peer.SharedAddons, action.ActionId))
                    {
                        peer.Connection.Send(new GenericActionResultMessage(action.RequestId, GenericActionOutcome.Rejected, "This action capability was not negotiated."));
                        break;
                    }
                    GenericActionResultMessage actionResult = _genericActionEndpoint.Receive(
                        new AuthenticatedActionPeer(peer.PlayerId, peer.Owner, peer.Authenticated, peer.Phase == PeerPhase.Live), action);
                    if (peer.Connection.IsOpen) peer.Connection.Send(actionResult);
                    break;
                case ClientPoseMessage pose when peer.Phase == PeerPhase.Live:
                    if (_addons.TryPose(peer.PlayerId, out ClientPoseMessage addonPose))
                    {
                        HostPlayerAuthority.AcceptAddonPose(peer.PlayerId, addonPose);
                        HostPlayerAuthority.AcceptAddonLook(peer.PlayerId, pose);
                        _commands.UpdatePose(peer.PlayerId, addonPose);
                        break;
                    }
                    if (!HostPlayerAuthority.TryAcceptPose(peer.PlayerId, pose, out MotionPoseAssessment motion))
                    {
                        LogMovementRejection(peer, pose, motion);
                        throw new InvalidDataException($"Client movement rejected: {motion.Rejection}; position={pose.Position}, velocity={pose.Velocity}.");
                    }
                    if (motion.HorizontalSpeed > motion.HorizontalSpeedLimit)
                        LogMovementSanitization(peer, pose, motion);
                    ClientPoseMessage authorizedPose = HostPlayerAuthority.RequirePose(peer.PlayerId);
                    _avatars.ApplyPose(peer.Avatar, authorizedPose);
                    _commands.UpdatePose(peer.PlayerId, authorizedPose);
                    peer.LastPosition = GatInterop.ToUnity(authorizedPose.Position);
                    peer.LastYawDegrees = authorizedPose.YawDegrees;
                    break;
                case ClientReadyMessage ready when ready.TransferId < peer.TransferId:
                    break;
                case ClientReadyMessage ready when peer.Phase == PeerPhase.AwaitingReady && ready.TransferId == peer.TransferId:
                    HandleReady(peer);
                    break;
                case ClientPoseMessage _:
                    break;
                case CommandIntentMessage intent when peer.Phase == PeerPhase.Live:
                    ExecuteNativeCommand(peer.PlayerId, intent);
                    break;
                case HoldMessage hold when peer.Phase == PeerPhase.Live:
                    HandleHold(peer, hold);
                    break;
                case WorldEditMessage _:
                    throw new InvalidDataException("Clients cannot submit authoritative world edits.");
                case WaterViewMessage view when peer.Phase == PeerPhase.Live:
                    ClientPoseMessage origin = HostPlayerAuthority.RequirePose(peer.PlayerId);
                    if (Math.Abs(view.X + 144 - origin.Position.X) > 64 || Math.Abs(view.Z + 144 - origin.Position.Z) > 64) throw new InvalidDataException("Water view is outside the authorized camera area.");
                    if (peer.WaterView == null || peer.WaterView.X != view.X || peer.WaterView.Z != view.Z)
                    {
                        peer.WaterView = view;
                        peer.WaterTiles.Reset();
                        peer.WaterGeneration++;
                        peer.NextWater = 0;
                    }
                    break;
                case ToolEditsMessage edits when peer.Phase == PeerPhase.Live:
                    WorldEdits.HostToolAuthority.Apply(peer.PlayerId, peer.Name, edits, _rigids, _log, InvalidateReplicas);
                    break;
                case MamaToggleMessage toggle when peer.Phase == PeerPhase.Live:
                    HostPlayerAuthority.RequireOwnedAgent(peer.PlayerId, toggle.Agent);
                    HostPlayerAuthority.RequireNear(peer.PlayerId, HostPlayerAuthority.AgentPosition(toggle.Agent), HostPlayerAuthority.HandReach);
                    if (Holds.MamaLookup.TryFind(toggle.Agent, out WolfMama mama))
                    {
                        GameApiCompatibility.ToggleMama(mama);
                        peer.Connection.Send(new CommandAudioMessage(CommandAudioCatalog.ForLockState(!mama.AllowFollow)));
                        _log.LogInfo($"[Mama] '{peer.Name}' {(mama.AllowFollow ? "unlocked" : "locked")} {mama.name}");
                    }
                    break;
                case BedRequestMessage bed when peer.Phase == PeerPhase.Live:
                    _night.HandleBedRequest(peer.PlayerId, bed.Ready);
                    break;
                default:
                    string identity = message is ClientReadyMessage ack ? $" transfer {ack.TransferId}, expected {peer.TransferId}" : string.Empty;
                    _log.LogWarning($"[Host] #{peer.PlayerId} sent {message.Type}{identity} during {peer.Phase}; disconnecting");
                    peer.Connection.Close($"protocol violation: {message.Type} during {peer.Phase}");
                    break;
            }
        }
    }

    private void LogMovementRejection(Peer peer, ClientPoseMessage pose, MotionPoseAssessment diagnostics)
    {
        LogMovementDiagnostic("MovementReject", peer, pose, diagnostics, rejected: true);
    }

    private void LogMovementSanitization(Peer peer, ClientPoseMessage pose, MotionPoseAssessment diagnostics)
    {
        LogMovementDiagnostic("MovementSanitized", peer, pose, diagnostics, rejected: false);
    }

    private void LogMovementDiagnostic(string eventName, Peer peer, ClientPoseMessage pose, MotionPoseAssessment diagnostics, bool rejected)
    {
        MotionMoveDiagnostics movement = diagnostics.Movement;
        float displacementSpeed = movement.ElapsedSeconds > 0
            ? movement.HorizontalDistance / (float)movement.ElapsedSeconds
            : float.PositiveInfinity;
        NetVector3 authorizedVelocity = movement.GetAuthorizedVelocity(diagnostics.HorizontalSpeedLimit);
        bool avatarValid = World.gat != null && World.gat.IsHandleValid(peer.Avatar);
        string avatarPosition = avatarValid
            ? World.gat!.GetInstanceMatrixImmediate(peer.Avatar)?.GetColumn(3).ToString() ?? "unavailable"
            : "invalid-handle";
        WorldSerializer? serializer = World.worldSerializer;
        string identity = Core.Security.PlayerIdentity.Fingerprint(peer.PublicKey);

        string diagnostic =
            $"[{eventName}] playerId={peer.PlayerId} name='{peer.Name}' identity={identity} family='{peer.Owner}' " +
            $"authenticated={peer.Authenticated} phase={peer.Phase} transfer={peer.TransferId} avatar={peer.Avatar} " +
            $"authoritative={movement.Origin} received={pose.Position} reportedVelocity={pose.Velocity} authorizedVelocity={authorizedVelocity} " +
            $"elapsed={movement.ElapsedSeconds:0.000000}s planarDistance={movement.HorizontalDistance:0.00} " +
            $"computedPlanarSpeed={displacementSpeed:0.00} reportedPlanarSpeed={diagnostics.HorizontalSpeed:0.00} " +
            $"speedLimit={diagnostics.HorizontalSpeedLimit:0.00} planarAllowance={movement.HorizontalAllowance:0.00} " +
            $"verticalDelta={movement.VerticalDistance:0.00} verticalAllowance={movement.VerticalAllowance:0.00} " +
            $"reason={(rejected ? diagnostics.Rejection.ToString() : "reported-velocity-capped-to-host-measured-movement")} " +
            $"budgetReason={movement.Rejection} context={{worldReady={LocalPlayer.IsWorldReady}, loading={serializer?.isLoadingInProgress}, " +
            $"worldBusy={serializer?.IsBusy}, provisioning={_provisioningActive}, buildManager={World.buildPlanManager != null}, " +
            $"avatarPosition={avatarPosition}, gameTime={GameClock.Now:0.000000}}}";
        if (rejected) _log.LogWarning(diagnostic);
        else _log.LogInfo(diagnostic);
    }

    private void RefuseAddonRestrictedAction(Peer peer, INetMessage message)
    {
        const string detail = "This action is unavailable while a multiplayer addon controls your character.";
        if (message is GenericActionRequestMessage action)
        {
            peer.Connection.Send(new GenericActionResultMessage(action.RequestId, GenericActionOutcome.Rejected, detail));
            return;
        }
        if (message is HoldMessage hold)
        {
            peer.Connection.Send(new HoldResultMessage(hold.Target, false, detail));
            return;
        }
        (int available, int total) = PoolFor(peer);
        peer.Connection.Send(new CommandFeedbackMessage(available, total, CommandOutcome.Refused, detail));
    }

    private void HandleHello(Peer peer, HelloMessage hello)
    {
        if (!ProtocolConstants.IsCompatibleVersion(hello.ProtocolVersion))
        {
            Reject(peer, $"Mod version mismatch: host speaks protocol {ProtocolConstants.Version}, you speak {hello.ProtocolVersion}.");
            return;
        }
        if (!GameBuildCompatibility.AreCompatible(FamilyTimeMultiplayer.Game.GameBuildIdentity.Current, hello.GameBuildId))
        {
            Reject(peer, $"Game build mismatch: host runs {FamilyTimeMultiplayer.Game.GameBuildIdentity.Current}, you run {hello.GameBuildId}.");
            return;
        }
        if (!AddonCapabilities.AreCompatible(Addons.SessionAddonRegistry.Descriptors, hello.Addons))
        {
            Reject(peer, "A required multiplayer addon is missing or differs in version/content. Optional local addons may differ.");
            return;
        }
        peer.SharedAddons = AddonCapabilities.Shared(Addons.SessionAddonRegistry.Descriptors, hello.Addons);
        Core.Security.PlayerIdentity.ValidatePublicKey(hello.PublicKey);
        peer.RequestedName = string.IsNullOrWhiteSpace(hello.PlayerName) ? $"Player {peer.PlayerId}" : hello.PlayerName.Trim();
        peer.Name = UniqueName(peer, peer.RequestedName);
        peer.PublicKey = hello.PublicKey;
        peer.IdentityHello = hello;
        peer.IdentityChallenge = Core.Security.PlayerIdentity.CreateChallenge();
        peer.Phase = PeerPhase.AwaitingIdentityProof;
        peer.Connection.Send(new IdentityChallengeMessage(peer.IdentityChallenge));
    }

    private void HandleIdentityProof(Peer peer, IdentityProofMessage proof)
    {
        if (peer.IdentityChallenge == null || !Core.Security.PlayerIdentity.VerifyHandshake(peer.IdentityChallenge, peer.IdentityHello!, proof.Signature)) throw new InvalidDataException("Player identity proof failed.");
        byte[] nonce = peer.IdentityChallenge;
        peer.IdentityChallenge = null;
        peer.IdentityHello = null;
        if (_peers.Any(other => !ReferenceEquals(peer, other) && other.Authenticated && other.Connection.IsOpen && other.PublicKey == peer.PublicKey))
        {
            Reject(peer, "This player identity is already connected.");
            return;
        }
        peer.ClientVerifier = new Core.Security.SignedClientVerifier(peer.PublicKey, nonce);
        peer.Authenticated = true;
        if (peer.Connection is TcpNetConnection transport) transport.Admit();
        peer.Phase = PeerPhase.AwaitingProvision;
        _log.LogInfo($"[Host] '{peer.Name}' authenticated as {Core.Security.PlayerIdentity.Fingerprint(peer.PublicKey)}, queued for world transfer");
    }

    public sealed class FamilyRestoreRequest
    {
        public FamilyRestoreRequest(int playerId, string playerName, string familyName, string fingerprint) { PlayerId = playerId; PlayerName = playerName; FamilyName = familyName; Fingerprint = fingerprint; }
        public int PlayerId { get; }
        public string PlayerName { get; }
        public string FamilyName { get; }
        public string Fingerprint { get; }
    }
    public enum FamilyRestoreChoice { Restore, NewFamily, Reject }
    public IReadOnlyList<FamilyRestoreRequest> PendingFamilyRestores => Families == null || World.worldSerializer?.isLoadingInProgress == true ? Array.Empty<FamilyRestoreRequest>() : _peers.Where(peer => peer.Phase == PeerPhase.AwaitingFamilyApproval && peer.Connection.IsOpen)
        .Select(peer => new FamilyRestoreRequest(peer.PlayerId, peer.Name, peer.RequestedName, Core.Security.PlayerIdentity.Fingerprint(peer.PublicKey))).ToArray();
    public void DecideFamilyRestore(int playerId, FamilyRestoreChoice choice)
    {
        Peer peer = _peers.SingleOrDefault(candidate => candidate.PlayerId == playerId && candidate.Phase == PeerPhase.AwaitingFamilyApproval && candidate.Authenticated && candidate.Connection.IsOpen)
            ?? throw new InvalidOperationException("Family restore request is no longer pending.");
        if (Families == null) throw new InvalidOperationException("Separate family world is no longer active.");
        switch (choice)
        {
            case FamilyRestoreChoice.Restore:
                if (!Families.Identities.IsLegacy(peer.RequestedName)) { Reject(peer, "The family has already been assigned to another player identity."); return; }
                Families.Identities.BindLegacy(peer.PublicKey, peer.RequestedName);
                peer.Owner = peer.RequestedName;
                break;
            case FamilyRestoreChoice.NewFamily:
                peer.Owner = Families.Identities.CreateFamily(peer.PublicKey, peer.RequestedName);
                break;
            case FamilyRestoreChoice.Reject:
                Reject(peer, "Host declined the family restore request.");
                return;
            default: throw new ArgumentOutOfRangeException(nameof(choice));
        }
        peer.Phase = PeerPhase.AwaitingProvision;
    }

    private void Reject(Peer peer, string reason)
    {
        _log.LogWarning($"[Host] rejecting #{peer.PlayerId}: {reason}");
        peer.Connection.Send(new RejectMessage(reason));
        peer.Connection.Close(reason);
    }

    private void HandleReady(Peer peer)
    {
        PublishCommandLocks();
        List<INetMessage> backlog = peer.Backlog ?? new List<INetMessage>();
        peer.Backlog = null;
        foreach (GenericEntityStateBatchMessage batch in GenericEntityStateBatchMessage.CreateBatches(
            _genericStates.CaptureSnapshot().Where(state => _replicationCapabilities.SupportsState(peer.SharedAddons, state))))
            peer.Connection.Send(batch);
        foreach (INetMessage message in backlog)
        {
            peer.Connection.Send(message);
        }
        peer.Phase = PeerPhase.Live;
        peer.WaterView = null;
        peer.WaterTiles.Reset();
        peer.WaterGeneration++;
        peer.NextWater = 0;
        peer.WaterRefreshAt = 0;
        peer.Connection.Send(_day.Capture());
        peer.Connection.Send(new WorldEditMessage(new[] { WeatherStateBridge.Capture() }));
        Families?.RegisterPlayer(peer.PlayerId, peer.PublicKey);
        _commands.AddPlayer(peer.PlayerId, peer.Name, peer.Avatar);
        peer.LastPool = null;
        peer.LastFamilyStatus = null;
        peer.LastCommandMarkers = null;
        peer.LastCommandOutlines = null;
        _info.SendEverythingNext();
        _lastMamaState = string.Empty;
        if (Mamas != null)
        {
            peer.Connection.Send(Mamas.CaptureRoster());
        }
        _rosterDirty = true;
        _addons.PeerReady(peer.PlayerId, peer.SharedAddons);
        _log.LogInfo($"[Host] '{peer.Name}' is live (flushed {backlog.Count} queued world events)");
    }

    private void DropClosedPeers()
    {
        for (int i = _peers.Count - 1; i >= 0; i--)
        {
            Peer peer = _peers[i];
            if (peer.Connection.IsOpen)
            {
                continue;
            }
            _log.LogInfo($"[Host] '{peer.Name}' (#{peer.PlayerId}) left: {peer.Connection.CloseReason}");
            _addons.PeerLeft(peer.PlayerId);
            _commands.RemovePlayer(peer.PlayerId);
            HostPlayerAuthority.Remove(peer.PlayerId);
            _holds.ReleaseAll(peer.PlayerId);
            Families?.UnregisterPlayer(peer.PlayerId);
            _avatars.Despawn(peer.Avatar);
            peer.ClientVerifier?.Dispose();
            _peers.RemoveAt(i);
            _rosterDirty = true;
        }
    }

    private void StartNextProvisioning()
    {
        if (_provisioningActive || !LocalPlayer.IsWorldReady)
        {
            return;
        }
        Peer? next = _peers.FirstOrDefault(p => p.Phase == PeerPhase.AwaitingProvision && p.Connection.IsOpen);
        if (next == null)
        {
            return;
        }
        if (Families != null && string.IsNullOrEmpty(next.Owner))
        {
            string? owner = Families.Identities.OwnerOf(next.PublicKey);
            if (owner != null) next.Owner = owner;
            else if (Families.Identities.IsLegacy(next.RequestedName))
            {
                next.Phase = PeerPhase.AwaitingFamilyApproval;
                next.Connection.Send(new FamilyApprovalPendingMessage());
                _log.LogInfo($"[Family] host approval required to restore '{next.RequestedName}' for identity {Core.Security.PlayerIdentity.Fingerprint(next.PublicKey)}");
                return;
            }
            else next.Owner = Families.Identities.CreateFamily(next.PublicKey, next.RequestedName);
        }
        _provisioningActive = true;
        next.Phase = PeerPhase.Provisioning;
        next.TransferId++;
        _coroutines.Run(GuardProvisioning(next, next.TransferId));
    }

    private IEnumerator GuardProvisioning(Peer peer, long transferId)
    {
        IEnumerator routine = ProvisionRoutine(peer, transferId);
        try
        {
            while (true)
            {
                bool continuing;
                try { continuing = routine.MoveNext(); }
                catch (Exception exception)
                {
                    _log.LogError($"[Host] provisioning exception: {exception}");
                    AbortProvisioning(peer, "Host could not prepare the world; see the host log.");
                    yield break;
                }
                if (!continuing) yield break;
                yield return routine.Current;
            }
        }
        finally { (routine as IDisposable)?.Dispose(); }
    }

    private IEnumerator ProvisionRoutine(Peer peer, long transferId)
    {
        try
        {
            string? failure = null;
            bool avatarReady = false;
            _avatars.Spawn(handle =>
            {
                if (transferId != peer.TransferId) { avatarReady = true; return; }
                peer.Avatar = handle;
                HostPlayerAuthority.Register(peer.PlayerId, handle);
                avatarReady = true;
            }, error =>
            {
                failure = error;
                avatarReady = true;
            });
            while (!avatarReady)
            {
                yield return null;
            }
            if (transferId != peer.TransferId) yield break;
            if (failure != null || !peer.Connection.IsOpen)
            {
                AbortProvisioning(peer, failure ?? "player left during avatar creation");
                yield break;
            }

            peer.Backlog = new List<INetMessage>();
            peer.BacklogBytes = 0;
            bool? saved = null;
            WorldSerializer serializer = GatInterop.RequireAlive(World.worldSerializer, "World.worldSerializer is missing.");
            serializer.Save(WorldFiles.HostSnapshotMapName, success => saved = success);
            while (saved == null)
            {
                yield return null;
            }
            if (transferId != peer.TransferId) yield break;
            if (saved != true || !peer.Connection.IsOpen)
            {
                AbortProvisioning(peer, saved == true ? "player left during world save" : "host could not save the world for transfer");
                yield break;
            }

            RigidIndexMapMessage rigidIndexMap = _rigids.CaptureIndexMap();
            string snapshotPath = WorldFiles.PathFor(WorldFiles.HostSnapshotMapName);
            long snapshotLength = new FileInfo(snapshotPath).Length;
            if (snapshotLength <= 0 || snapshotLength > ProtocolConstants.MaxWorldBytes)
            {
                AbortProvisioning(peer, $"World size exceeds the {ProtocolConstants.MaxWorldBytes / (1024 * 1024)} MiB transfer limit.");
                yield break;
            }
            byte[] world = File.ReadAllBytes(snapshotPath);
            ProtocolConstants.RequireWorldLength(world.LongLength);
            peer.Connection.Send(new WelcomeMessage(peer.PlayerId, peer.Avatar.stableIndex, world.LongLength, _familyMode, transferId, _addonWorldGeneration, Addons.SessionAddonRegistry.Descriptors));
            for (long offset = 0; offset < world.LongLength; offset += ProtocolConstants.WorldChunkBytes)
            {
                int length = (int)Math.Min(ProtocolConstants.WorldChunkBytes, world.LongLength - offset);
                var chunk = new byte[length];
                Buffer.BlockCopy(world, (int)offset, chunk, 0, length);
                while (peer.Connection.IsOpen && peer.Connection.PendingSendFrames > 8) yield return null;
                if (!peer.Connection.IsOpen || transferId != peer.TransferId) yield break;
                peer.Connection.Send(new WorldChunkMessage(offset, chunk));
            }
            peer.Connection.Send(new EntityLifecycleMapMessage(_entityRegistry.WorldGeneration, _entityRegistry.CaptureLifecycle()));
            peer.Connection.Send(new WorldCompleteMessage(Crc32.Compute(world)));
            peer.Connection.Send(rigidIndexMap);
            peer.Phase = PeerPhase.AwaitingReady;
            _rosterDirty = true;
            _log.LogInfo($"[Host] sent {world.LongLength / (1024 * 1024)} MB world to '{peer.Name}', avatar {peer.Avatar}");
        }
        finally
        {
            _provisioningActive = false;
        }
    }

    private void AbortProvisioning(Peer peer, string reason)
    {
        _log.LogError($"[Host] provisioning '{peer.Name}' failed: {reason}");
        peer.Backlog = null;
        if (peer.Connection.IsOpen)
        {
            peer.Connection.Send(new RejectMessage(reason));
            peer.Connection.Close(reason);
        }
    }

    private void PublishRosterIfDirty()
    {
        if (!_rosterDirty)
        {
            return;
        }
        _rosterDirty = false;
        var entries = new List<RosterEntry>
        {
            new RosterEntry(PlayerRosterMessage.HostPlayerId, _hostName, LocalPlayer.PossessedHandle.stableIndex),
        };
        entries.AddRange(_peers
            .Where(p => !p.Avatar.isNull && (p.Phase == PeerPhase.Live || p.Phase == PeerPhase.AwaitingReady))
            .Select(p => new RosterEntry(p.PlayerId, p.Name, p.Avatar.stableIndex)));
        _roster = entries;
        var message = new PlayerRosterMessage(entries);
        foreach (Peer peer in _peers.Where(p => p.Phase == PeerPhase.Live || p.Phase == PeerPhase.AwaitingReady))
        {
            peer.Connection.Send(message);
        }
    }

    private void StreamSnapshots(double now)
    {
        if (now < _nextSnapshotTime || _sampler.InFlight || World.gat == null || !_peers.Any(p => p.Phase == PeerPhase.Live))
        {
            return;
        }
        _nextSnapshotTime = now + _settings.SnapshotInterval;
        RigidSnapshotMessage rigidSnapshot = _rigids.Capture(FamilyTimeMultiplayer.Game.GameClock.Now);
        _sampler.Request(agents =>
        {
            var message = new AgentSnapshotMessage(FamilyTimeMultiplayer.Game.GameClock.Now, agents);
            foreach (Peer peer in _peers)
            {
                if (peer.Phase == PeerPhase.Live && peer.Connection.PendingSendFrames < MaxQueuedFramesBeforeSkippingSnapshots)
                {
                    peer.Connection.Send(message);
                    peer.Connection.Send(rigidSnapshot);
                }
            }
        }, error => _log.LogWarning($"[Host] {error}"));
    }

    private void BroadcastPool(double now)
    {
        if (World.commandOwnershipService == null)
        {
            return;
        }
        bool periodic = now >= _nextPoolBroadcast;
        if (periodic)
        {
            _nextPoolBroadcast = now + PoolBroadcastInterval;
        }
        foreach (Peer peer in _peers.Where(p => p.Phase == PeerPhase.Live))
        {
            (int available, int total) pool = PoolFor(peer);
            if (!periodic && peer.LastPool == pool)
            {
                continue;
            }
            peer.LastPool = pool;
            peer.Connection.Send(new CommandFeedbackMessage(pool.available, pool.total, CommandOutcome.None, string.Empty));
        }
    }

    private double _nextHealth;

    private void LogHealth(double now)
    {
        if (now < _nextHealth)
        {
            return;
        }
        _nextHealth = now + 10.0;
        WorldSerializer? serializer = World.worldSerializer;
        string peers = string.Join("; ", _peers.Select(p => $"'{p.Name}' {p.Phase} open={p.Connection.IsOpen} queued={p.Connection.PendingSendFrames} lastRx={now - p.LastReceivedAt:0.0}s"));
        _log.LogInfo($"[Health] host fps={1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f):0} timeScale={FamilyTimeMultiplayer.Game.GameApiCompatibility.TimeScale:0.##} worldBusy={serializer?.IsBusy} loading={serializer?.isLoadingInProgress} provisioning={_provisioningActive} relay={_relay?.State} peers=[{peers}]");
    }

    private void SendToLive(INetMessage message)
    {
        foreach (Peer peer in _peers.Where(p => p.Phase == PeerPhase.Live))
        {
            peer.Connection.Send(message);
        }
    }

    private void HandleHold(Peer peer, HoldMessage hold)
    {
        switch (hold.Phase)
        {
            case HoldPhase.Begin:
                string? refusal = _holds.TryBegin(peer.PlayerId, peer.Name, hold);
                peer.Connection.Send(new HoldResultMessage(hold.Target, refusal == null, refusal ?? string.Empty));
                break;
            case HoldPhase.Update:
                _holds.Update(peer.PlayerId, hold);
                break;
            case HoldPhase.Settle:
                _holds.Settle(peer.PlayerId, hold);
                break;
            case HoldPhase.End:
                _holds.End(peer.PlayerId, hold);
                break;
        }
    }

    private void BroadcastMamaState(double now)
    {
        if (now < _nextMamaState)
        {
            return;
        }
        _nextMamaState = now + 0.5;
        MamaStateEntry[] mamas = Holds.MamaLookup.All()
            .Where(m => !m.FakeGATHandle.isNull)
            .Select(m => new MamaStateEntry(m.FakeGATHandle.stableIndex, m.AllowFollow))
            .ToArray();
        string key = string.Join(",", mamas.Select(m => $"{m.Agent}:{m.AllowFollow}"));
        if (key == _lastMamaState)
        {
            return;
        }
        _lastMamaState = key;
        var message = new MamaStateMessage(mamas);
        foreach (Peer peer in _peers.Where(p => p.Phase == PeerPhase.Live))
        {
            peer.Connection.Send(message);
        }
    }

    private (int Available, int Total) PoolFor(Peer peer)
    {
        if (Families != null)
        {
            string owner = Families.OwnerOfPlayer(peer.PlayerId);
            return (Families.AvailableFor(owner), Families.ChildrenOf(owner));
        }
        CommandOwnershipService ownership = _commands.Ownership;
        return (ownership.AvailableWorkerCount, ownership.WorkerTemplateAgentCount);
    }

    private void TickFamilies(double now)
    {
        if (_familyMode == Core.Protocol.FamilyMode.Separate && Families == null)
        {
            return;
        }
        if (Families != null)
        {
            GiveStarterFamilies();
        }
        if (now < _nextFamilyStatus || World.stateDisplayUI == null)
        {
            return;
        }
        _nextFamilyStatus = now + FamilyStatusInterval;
        FamilyHudCounts shared = FamilyHud.ReadDisplayedCounts();
        foreach (Peer peer in _peers.Where(p => p.Phase == PeerPhase.Live))
        {
            FamilyHudCounts counts = Families != null ? Families.CountsFor(Families.OwnerOfPlayer(peer.PlayerId)) : shared;
            ushort[]? members = Families?.MembersOf(Families.OwnerOfPlayer(peer.PlayerId)).OrderBy(id => id).ToArray();
            bool sameMembers = members == null ? peer.LastFamilyMembers == null : peer.LastFamilyMembers != null && members.SequenceEqual(peer.LastFamilyMembers);
            if (peer.LastFamilyStatus.HasValue && peer.LastFamilyStatus.Value.Equals(counts) && sameMembers)
            {
                continue;
            }
            peer.LastFamilyStatus = counts;
            peer.LastFamilyMembers = members;
            peer.Connection.Send(new FamilyStatusMessage(counts.Children, counts.Hungry, counts.NotSleeping, members));
        }
    }

    private void GiveStarterFamilies()
    {
        foreach (Peer peer in _peers.Where(p => p.Phase == PeerPhase.Live && !p.StarterFamilyHandled && p.LastPosition.HasValue))
        {
            peer.StarterFamilyHandled = true;
            if (Families!.HasFamily(peer.Owner))
            {
                _log.LogInfo($"[Family] '{peer.Name}' rejoined and keeps {Families.ChildrenOf(peer.Owner)} child(ren)");
            }
            else
            {
                Families.SpawnStarterFamily(peer.Owner, peer.LastPosition!.Value);
            }
            if (Mamas!.EnsureMamaFor(peer.Owner, peer.LastPosition!.Value))
            {
                BroadcastMamas();
            }
        }
    }

    private void BroadcastMamas()
    {
        MamaRosterMessage roster = Mamas!.CaptureRoster();
        foreach (Peer peer in _peers.Where(p => p.Phase == PeerPhase.Live))
        {
            peer.Connection.Send(roster);
        }
    }

    private string UniqueName(Peer self, string requested)
    {
        bool Taken(string name) =>
            string.Equals(name, _hostName, StringComparison.Ordinal)
            || _peers.Any(p => !ReferenceEquals(p, self) && p.Phase != PeerPhase.AwaitingHello && string.Equals(p.Name, name, StringComparison.Ordinal));
        if (!Taken(requested))
        {
            return requested;
        }
        string unique = $"{requested} ({self.PlayerId})";
        _log.LogInfo($"[Host] name '{requested}' is taken; #{self.PlayerId} plays as '{unique}'");
        return unique;
    }

    public IReadOnlyList<NightParticipant> RemotePlayers =>
        _peers.Where(p => p.Phase == PeerPhase.Live).Select(p => new NightParticipant(p.PlayerId, p.Name, p.LastPosition)).ToArray();

    public void SendTo(int playerId, INetMessage message)
    {
        Peer peer = _peers.FirstOrDefault(p => p.PlayerId == playerId)
            ?? throw new InvalidOperationException($"No connected player #{playerId} to send {message.Type} to.");
        peer.Connection.Send(message);
    }

    private void StreamCommandMarkers(double now)
    {
        if (now < _nextCommandMarkers) return;
        _nextCommandMarkers = now + 0.1;
        foreach (Peer peer in _peers.Where(p => p.Phase == PeerPhase.Live))
        {
            CommandMarkersMessage message = _commands.CaptureMarkers(peer.PlayerId);
            byte[] encoded = MessageCodec.Encode(message);
            if (peer.LastCommandMarkers == null || !encoded.SequenceEqual(peer.LastCommandMarkers))
            {
                peer.Connection.Send(message);
                peer.LastCommandMarkers = encoded;
            }
            CommandOutlinesMessage outlines = _commands.CaptureOutlines(peer.PlayerId, _rigids);
            byte[] outlineFrame = MessageCodec.Encode(outlines);
            if (peer.LastCommandOutlines == null || !outlineFrame.SequenceEqual(peer.LastCommandOutlines))
            {
                peer.Connection.Send(outlines);
                peer.LastCommandOutlines = outlineFrame;
            }
        }
    }

    private void StreamDayState(double now)
    {
        if (now < _nextDayStateTime || World.dayManager == null)
        {
            return;
        }
        _nextDayStateTime = now + DayStateInterval;
        DayStateMessage state = _day.Capture();
        foreach (Peer peer in _peers.Where(p => p.Phase == PeerPhase.Live))
        {
            peer.Connection.Send(state);
        }
    }
}
