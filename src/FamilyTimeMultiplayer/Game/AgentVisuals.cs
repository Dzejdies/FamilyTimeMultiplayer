using System;
using System.Collections.Generic;
using System.Linq;
using FamilyTimeMultiplayer.Core.Interpolation;
using FamilyTimeMultiplayer.Core.Protocol;
using GAT;
using Unity.Collections;
using UnityEngine;
using AnimationEvent = FamilyTimeMultiplayer.Core.Protocol.AnimationEvent;
using ProtocolAnimationEventKind = FamilyTimeMultiplayer.Core.Protocol.AnimationEventKind;
using UnityEngine.Rendering;
using GatSimulation = global::GAT.GAT;

namespace FamilyTimeMultiplayer.Game;

public sealed class AgentInfoSampler : IDisposable
{
    private const double Interval = 0.25;

    private readonly Dictionary<ushort, uint> _lastSent = new Dictionary<ushort, uint>();
    private double _next;
    private bool _inFlight;
    private int _layoutVersion;
    private GatSimulation? _subscribed;
    private Delegate? _layoutChangedHandler;

    public void SendEverythingNext() => _lastSent.Clear();

    public void Tick(double now, Action<AgentInfoMessage> send)
    {
        GatSimulation? gat = World.gat;
        if (gat != _subscribed)
        {
            Unsubscribe();
            if (gat != null)
            {
                _layoutChangedHandler = GatInterop.SubscribeLayoutChanged(gat, OnLayoutChanged);
            }
            _subscribed = gat;
        }
        ComputeBuffer? buffer = gat != null ? gat.GetInstanceInfoBuffer() : null;
        if (now < _next || _inFlight || gat == null || buffer == null || gat.instanceCount == 0)
        {
            return;
        }
        _next = now + Interval;
        int count = gat.instanceCount;
        int layout = _layoutVersion;
        _inFlight = true;
        AsyncGPUReadback.Request(buffer, count * sizeof(uint), 0, request =>
        {
            _inFlight = false;
            if (request.hasError || layout != _layoutVersion || World.gat == null || World.gat.instanceCount < count)
            {
                return;
            }
            NativeArray<uint> data = request.GetData<uint>();
            var changed = new List<AgentInfoEntry>();
            for (int physical = 0; physical < count; physical++)
            {
                ushort stable = World.gat.GetHandle((ushort)physical).stableIndex;
                uint visual = AgentInfoBits.Visual(data[physical]);
                if (!_lastSent.TryGetValue(stable, out uint previous) || previous != visual)
                {
                    _lastSent[stable] = visual;
                    changed.Add(new AgentInfoEntry(stable, data[physical]));
                }
            }
            if (changed.Count > 0)
            {
                send(new AgentInfoMessage(changed));
            }
        });
    }

    private void OnLayoutChanged() => _layoutVersion++;

    private void Unsubscribe()
    {
        if (_subscribed != null)
        {
            if (_layoutChangedHandler != null) GatInterop.UnsubscribeLayoutChanged(_subscribed, _layoutChangedHandler);
        }
        _subscribed = null;
        _layoutChangedHandler = null;
    }

    public void Dispose() => Unsubscribe();
}

public static class AgentVisualsApplier
{
    public static void ApplyInfo(AgentInfoMessage message, AgentIdentityMap identities, ushort ownLocal)
    {
        GatSimulation gat = GatInterop.RequireGat();
        foreach (AgentInfoEntry entry in message.Agents)
        {
            if (identities.TryGetLocal(entry.HostStableIndex, out ushort local) && local != ownLocal)
            {
                gat.SetInstanceInfo(GatInterop.ToHandle(local), GatInterop.CreateInstanceInfo(AgentInfoBits.ForPuppet(entry.Info)));
            }
        }
    }

    public static void ApplyAnimations(AnimationEventsMessage message, AgentIdentityMap identities)
    {
        GATAnimationManager? animations = World.gatAnimationManager;
        GatSimulation? gat = World.gat;
        if (animations == null || gat == null)
        {
            return;
        }
        AnimationRelay.Applying = true;
        try
        {
            foreach (AnimationEvent animation in message.Events)
            {
                Handle[] handles = animation.HostAgents
                    .Select(host => identities.TryGetLocal(host, out ushort local) ? GatInterop.ToHandle(local) : Handle.Null)
                    .Where(h => !h.isNull)
                    .ToArray();
                int? motionFilter = animation.MotionSetFilter < 0 ? (int?)null : animation.MotionSetFilter;
                AnimationEnum? onlyType = animation.OnlyAnimationType < 0 ? (AnimationEnum?)null : (AnimationEnum)animation.OnlyAnimationType;
                switch (animation.Kind)
                {
                    case ProtocolAnimationEventKind.Play when handles.Length > 0:
                        animations.PlayAnimations(handles, (AnimationEnum)animation.Primary, motionFilter, animation.OnlyWhenIdling, onlyType,
                            animation.Secondary >= 0 ? gat.GetTemplate(animation.Secondary) : null);
                        break;
                    case ProtocolAnimationEventKind.PlayShared when handles.Length > 0:
                        animations.PlayAnimationsSharedTemplate(handles, animation.Primary, animation.Secondary, motionFilter, animation.OnlyWhenIdling, onlyType);
                        break;
                    case ProtocolAnimationEventKind.PlayWithinDistance:
                        animations.PlayAnimationsWithinDistance(GatInterop.ToUnity(animation.Origin), animation.Radius, (AnimationEnum)animation.Primary, animation.Secondary, motionFilter, animation.OnlyWhenIdling, onlyType);
                        break;
                    case ProtocolAnimationEventKind.Complete when handles.Length > 0:
                        Template template = gat.GetTemplate(animation.Secondary);
                        if (template != null)
                        {
                            animations.CompleteAnimations(handles, (AnimationEnum)animation.Primary, template);
                        }
                        break;
                    case ProtocolAnimationEventKind.MotionSets when handles.Length > 0:
                        animations.SetInstanceMotionSets(handles, animation.Primary, animation.Secondary, animation.Flag);
                        break;
                }
            }
        }
        finally
        {
            AnimationRelay.Applying = false;
        }
    }
}

public static class AnimationRelay
{
    private static readonly List<AnimationEvent> Pending = new List<AnimationEvent>();

    public static int Depth { get; set; }

    public static bool Applying { get; set; }

    public static bool Records => Depth == 0 && !Applying && WorldEdits.WorldEditRelay.HostRecords;

    public static ushort[] Stable(Handle[] handles) => handles.Where(h => !h.isNull).Select(h => h.stableIndex).ToArray();

    public static void Record(AnimationEvent animation) => Pending.Add(animation);

    public static IReadOnlyList<AnimationEventsMessage> TakeBatches()
    {
        if (Pending.Count == 0)
        {
            return Array.Empty<AnimationEventsMessage>();
        }
        var batches = new List<AnimationEventsMessage>();
        for (int offset = 0; offset < Pending.Count; offset += AnimationEventsMessage.MaxEvents)
        {
            batches.Add(new AnimationEventsMessage(Pending.Skip(offset).Take(AnimationEventsMessage.MaxEvents).ToArray()));
        }
        Pending.Clear();
        return batches;
    }

    public static void Reset() => Pending.Clear();
}
