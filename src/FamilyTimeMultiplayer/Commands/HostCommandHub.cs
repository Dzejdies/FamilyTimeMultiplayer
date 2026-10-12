using System;
using System.Collections.Generic;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Core.Commands;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Commands;

public sealed class HostCommandHub : IDisposable
{
    private readonly ManualLogSource _log;
    private readonly Dictionary<int, RemoteCommander> _commanders = new Dictionary<int, RemoteCommander>();
    private readonly Queue<(int PlayerId, CommandAudioMessage Message)> _audio = new Queue<(int, CommandAudioMessage)>();
    private bool _balancing;

    public HostCommandHub(ManualLogSource log)
    {
        _log = log;
        if (Current != null)
        {
            throw new InvalidOperationException("A HostCommandHub is already active.");
        }
        Current = this;
    }

    public static HostCommandHub? Current { get; private set; }

    public CommandOwnershipService Ownership =>
        GatInterop.RequireAlive(World.commandOwnershipService, "World.commandOwnershipService is missing.");

    public void AddPlayer(int playerId, string name, Handle avatar)
    {
        RemovePlayer(playerId);
        _commanders.Add(playerId, new RemoteCommander(_log, playerId, name, avatar));
        _log.LogInfo($"[Commands] '{name}' (#{playerId}) can now command kids through avatar {avatar}");
    }

    public void RemovePlayer(int playerId)
    {
        if (_commanders.TryGetValue(playerId, out RemoteCommander commander))
        {
            commander.Dispose();
            _commanders.Remove(playerId);
        }
    }

    public void UpdatePose(int playerId, ClientPoseMessage pose)
    {
        if (_commanders.TryGetValue(playerId, out RemoteCommander commander))
        {
            commander.UpdatePose(pose);
        }
    }

    internal void RunAsPlayer(int playerId, Action action)
    {
        if (!_commanders.TryGetValue(playerId, out RemoteCommander commander))
            throw new InvalidOperationException($"Player #{playerId} has no live remote commander.");
        using (CommanderContext.Enter(commander)) action();
    }

    internal void ValidateIntent(int playerId, CommandIntentMessage intent, HostRigidRegistry rigids)
    {
        if (!_commanders.TryGetValue(playerId, out RemoteCommander commander))
            throw new System.IO.InvalidDataException("Player has no live native commander.");
        commander.ValidateIntent(intent, rigids);
    }

    public void Handle(int playerId, CommandIntentMessage intent, HostRigidRegistry rigids, Action<CommandResult> report)
    {
        if (!_commanders.TryGetValue(playerId, out RemoteCommander commander))
        {
            report(CommandResult.Refused("you have no avatar in this world yet"));
            return;
        }
        commander.Handle(intent, rigids, result =>
        {
            _log.LogInfo($"[Commands] #{playerId} {intent.Kind}/{intent.TargetKind}: {result.Outcome} ({result.Detail}); free kids {Ownership.AvailableWorkerCount}");
            report(result);
        });
    }

    internal void SuspendInputGestures(int playerId)
    {
        if (_commanders.TryGetValue(playerId, out RemoteCommander commander)) commander.SuspendInputGestures();
        else if (playerId == PlayerRosterMessage.HostPlayerId)
        {
            GameApiCompatibility.CancelFollowGesture(HostControllers().Follow);
            GameApiCompatibility.StopLocalSelectionGesture(World.commandService);
        }
    }

    public void Tick()
    {
        foreach (RemoteCommander commander in _commanders.Values)
        {
            commander.Tick();
        }
    }

    public CommandOutlinesMessage CaptureOutlines(int playerId, HostRigidRegistry rigids) => _commanders.TryGetValue(playerId, out RemoteCommander commander)
        ? commander.CaptureOutlines(rigids)
        : new CommandOutlinesMessage(Array.Empty<CommandOutline>());

    public CommandMarkersMessage CaptureMarkers(int playerId) => _commanders.TryGetValue(playerId, out RemoteCommander commander)
        ? commander.CaptureMarkers()
        : new CommandMarkersMessage(Array.Empty<CommandMarker>());

    public void QueueAudio(int playerId, AudioGroup group)
    {
        if (group != null && _commanders.ContainsKey(playerId))
        {
            _audio.Enqueue((playerId, new CommandAudioMessage(CommandAudioCatalog.Identify(group))));
        }
    }

    public IEnumerable<(int PlayerId, CommandAudioMessage Message)> TakeAudio()
    {
        while (_audio.Count > 0)
        {
            yield return _audio.Dequeue();
        }
    }

    public void BalanceBeforeFollow(FollowController requester)
    {
        if (_balancing)
        {
            return;
        }
        _balancing = true;
        try
        {
            int requesterId = PlayerIdOf(requester);
            var followers = new Dictionary<int, int> { [PlayerRosterMessage.HostPlayerId] = HostFollowerCount() };
            foreach (KeyValuePair<int, RemoteCommander> entry in _commanders)
            {
                followers[entry.Key] = entry.Value.ReleasableFollowerCount;
            }
            IReadOnlyDictionary<int, int> take = FollowShareBalancer.TakeForRequester(requesterId, Ownership.AvailableWorkerCount, followers);
            foreach (KeyValuePair<int, int> donor in take)
            {
                int released = donor.Key == PlayerRosterMessage.HostPlayerId
                    ? ReleaseHostFollowers(donor.Value)
                    : _commanders[donor.Key].ReleaseFollowers(donor.Value);
                _log.LogInfo($"[Commands] follow split: #{requesterId} takes {released} kid(s) from #{donor.Key}");
            }
        }
        finally
        {
            _balancing = false;
        }
    }

    public void NotifyAgentGrabbed(GAT.Handle agent)
    {
        foreach (RemoteCommander commander in _commanders.Values)
        {
            commander.OnAgentGrabbed(agent);
        }
    }

    public IEnumerable<(int PlayerId, FollowController Follow)> FollowControllers()
    {
        yield return (PlayerRosterMessage.HostPlayerId, HostControllers().Follow);
        foreach (KeyValuePair<int, RemoteCommander> entry in _commanders)
        {
            yield return (entry.Key, entry.Value.Follow);
        }
    }

    public IReadOnlyDictionary<int, int> FollowerCounts()
    {
        var counts = new Dictionary<int, int> { [PlayerRosterMessage.HostPlayerId] = HostFollowerCount() };
        foreach (KeyValuePair<int, RemoteCommander> entry in _commanders)
        {
            counts[entry.Key] = entry.Value.ReleasableFollowerCount;
        }
        return counts;
    }

    public void Dispose()
    {
        foreach (RemoteCommander commander in _commanders.Values)
        {
            commander.Dispose();
        }
        _commanders.Clear();
        _audio.Clear();
        if (Current == this)
        {
            Current = null;
        }
    }

    private int PlayerIdOf(FollowController follow)
    {
        foreach (KeyValuePair<int, RemoteCommander> entry in _commanders)
        {
            if (ReferenceEquals(entry.Value.Follow, follow))
            {
                return entry.Key;
            }
        }
        return PlayerRosterMessage.HostPlayerId;
    }

    public static (FollowController Follow, PickupWorkerScheduler Pickup) HostControllers()
    {
        CommandService service = GatInterop.RequireAlive(World.commandService, "World.commandService is missing.");
        Traverse fields = Traverse.Create(service);
        return (fields.Field<FollowController>("_follow").Value, fields.Field<PickupWorkerScheduler>("_pickup").Value);
    }

    private static int HostFollowerCount()
    {
        (FollowController follow, PickupWorkerScheduler pickup) = HostControllers();
        return FollowGroups.ReleasableFollowers(follow, pickup).Count;
    }

    private static int ReleaseHostFollowers(int count)
    {
        (FollowController follow, PickupWorkerScheduler pickup) = HostControllers();
        return FollowGroups.Release(follow, pickup, count);
    }
}
