using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Core.Families;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.UI;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Families;

public readonly struct NightParticipant
{
    public NightParticipant(int playerId, string name, Vector3? position)
    {
        PlayerId = playerId;
        Name = name;
        Position = position;
    }

    public int PlayerId { get; }
    public string Name { get; }
    public Vector3? Position { get; }
}

public interface INightParticipants
{
    IReadOnlyList<NightParticipant> RemotePlayers { get; }

    void SendTo(int playerId, INetMessage message);
}

public sealed class HostNightCoordinator : IDisposable
{
    private static readonly MethodInfo BeginSleep =
        AccessTools.Method(typeof(BedManager), "BeginSleep")
        ?? throw new MissingMethodException(nameof(BedManager), "BeginSleep");

    private readonly ManualLogSource _log;
    private readonly INightParticipants _participants;
    private readonly int _hostPlayerId;
    private readonly string _hostName;
    private readonly BedReadiness _beds = new BedReadiness();
    private BedManager? _hostBed;
    private bool _allowNextBeginSleep;
    private string _lastPublished = string.Empty;

    public HostNightCoordinator(ManualLogSource log, INightParticipants participants, int hostPlayerId, string hostName)
    {
        if (Current != null)
        {
            throw new InvalidOperationException("A HostNightCoordinator is already active.");
        }
        _log = log;
        _participants = participants;
        _hostPlayerId = hostPlayerId;
        _hostName = hostName;
        Current = this;
    }

    public static HostNightCoordinator? Current { get; private set; }

    public bool HostIsReady => _beds.IsReady(_hostPlayerId);

    public bool RequestHostSleep(BedManager bed)
    {
        if (_allowNextBeginSleep)
        {
            _allowNextBeginSleep = false;
            return true;
        }
        if (_participants.RemotePlayers.Count == 0)
        {
            return true;
        }
        _hostBed = bed;
        _beds.MarkReady(_hostPlayerId);
        _log.LogInfo("[Night] host is in bed");
        Tick();
        return false;
    }

    public void HostGetsUp()
    {
        _beds.Cancel(_hostPlayerId);
        Tick();
    }

    public void HandleBedRequest(int playerId, bool ready)
    {
        if (ready)
        {
            _beds.MarkReady(playerId);
        }
        else
        {
            _beds.Cancel(playerId);
        }
        _log.LogInfo($"[Night] #{playerId} {(ready ? "is in bed" : "got up")}");
        Tick();
    }

    public void Tick()
    {
        IReadOnlyList<NightParticipant> remotes = _participants.RemotePlayers;
        var everyone = new List<int> { _hostPlayerId };
        everyone.AddRange(remotes.Select(p => p.PlayerId));
        IReadOnlyList<int> waiting = _beds.Waiting(everyone);
        bool anyoneReady = waiting.Count < everyone.Count;
        if (anyoneReady && waiting.Count == 0 && _hostBed != null)
        {
            StartNight(remotes);
            return;
        }
        Publish(remotes, anyoneReady ? waiting : Array.Empty<int>());
    }

    private void StartNight(IReadOnlyList<NightParticipant> remotes)
    {
        BedManager bed = _hostBed!;
        _beds.Reset();
        _hostBed = null;
        Publish(remotes, Array.Empty<int>());
        _log.LogInfo($"[Night] everyone ({remotes.Count + 1}) is in bed; the night begins");
        _allowNextBeginSleep = true;
        BeginSleep.Invoke(bed, null);
    }

    private void Publish(IReadOnlyList<NightParticipant> remotes, IReadOnlyList<int> waiting)
    {
        string NameOf(int id) => id == _hostPlayerId ? _hostName : remotes.FirstOrDefault(p => p.PlayerId == id).Name ?? $"#{id}";
        string[] waitingNames = waiting.Select(NameOf).ToArray();
        string key = string.Join("|", waiting) + "/" + string.Join("|", remotes.Select(p => p.PlayerId + (_beds.IsReady(p.PlayerId) ? "+" : "-")));
        if (key == _lastPublished)
        {
            return;
        }
        _lastPublished = key;
        PlayerNotice.SetSticky(waitingNames.Length == 0 ? null : HostIsReady
            ? $"In bed. Waiting for: {string.Join(", ", waitingNames)} (click the bed to get up)"
            : $"{string.Join(", ", remotes.Where(p => _beds.IsReady(p.PlayerId)).Select(p => p.Name))} went to bed - sleep when you are ready");
        foreach (NightParticipant player in remotes)
        {
            _participants.SendTo(player.PlayerId, new BedStatusMessage(_beds.IsReady(player.PlayerId), waitingNames));
        }
    }

    public void OnHostPlaysSleepMovies()
    {
        IReadOnlyList<NightParticipant> remotes = _participants.RemotePlayers;
        HostFamilyService? families = HostFamilyService.Current;
        if (families == null)
        {
            FamilyHudCounts counts = FamilyHud.ReadDisplayedCounts();
            FamilyNightOutcome shared = NightRules.Evaluate(new FamilyNightState(counts.Children, counts.NotSleeping, counts.Hungry), GameFamilyAccess.ChildGrowthMultiplier);
            foreach (NightParticipant player in remotes)
            {
                _participants.SendTo(player.PlayerId, ToMessage(shared));
            }
            _log.LogInfo($"[Night] shared family: missing={shared.ChildrenMissing} hungry={shared.ChildrenHungry} grows by {shared.GrownBy}");
            return;
        }
        var sleepers = new List<(string Owner, Vector3 Center)> { (families.HostOwner, LocalPlayer.Position) };
        sleepers.AddRange(remotes.Select(p => (families.OwnerOfPlayer(p.PlayerId), p.Position ?? LocalPlayer.Position)));
        IReadOnlyDictionary<string, FamilyNightOutcome> outcomes = families.PlanNight(sleepers);
        foreach (NightParticipant player in remotes)
        {
            _participants.SendTo(player.PlayerId, ToMessage(outcomes[families.OwnerOfPlayer(player.PlayerId)]));
        }
    }

    private static NightOutcomeMessage ToMessage(FamilyNightOutcome outcome) =>
        new NightOutcomeMessage(outcome.ChildrenMissing, outcome.ChildrenHungry, outcome.GrownBy);

    public void Dispose()
    {
        PlayerNotice.SetSticky(null);
        if (Current == this)
        {
            Current = null;
        }
    }
}
