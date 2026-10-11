using System;
using System.Collections.Generic;
using System.Linq;
using FamilyTimeMultiplayer.Core.Interpolation;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Commands;

public static class ClientCommandMarkers
{
    private static IReadOnlyList<CommandMarker> _markers = Array.Empty<CommandMarker>();
    private static AgentIdentityMap? _identities;
    private static FakeGATManager? _tracker;
    private static readonly HashSet<ushort> Tracked = new HashSet<ushort>();

    public static void Apply(CommandMarkersMessage message, AgentIdentityMap identities)
    {
        _markers = message.Markers;
        _identities = identities;
        RefreshTracking();
    }

    public static void Clear()
    {
        foreach (ushort local in Tracked) _tracker?.UnregisterHandle(GatInterop.ToHandle(local));
        Tracked.Clear();
        _tracker = null;
        _identities = null;
        _markers = Array.Empty<CommandMarker>();
    }

    public static void Draw(CommandService service)
    {
        if (!ClientCommandBridge.IsActive || _identities == null) return;
        RefreshTracking();
        CommandVisuals visuals = Traverse.Create(service).Field<CommandVisuals>("_visuals").Value;
        foreach (CommandMarker marker in _markers)
        {
            Vector3 position = GatInterop.ToUnity(marker.Position);
            if (marker.Kind == CommandMarkerKind.Attack)
            {
                if (!_identities.TryGetLocal(marker.HostTarget, out ushort local) || World.gat == null || !World.gat.IsHandleValid(GatInterop.ToHandle(local))) continue;
                if (_tracker != null && _tracker.TryGetPosition(GatInterop.ToHandle(local), out Vector3 targetPosition)) position = targetPosition + Vector3.up * 1.5f;
            }
            visuals.DrawHud(ToNative(marker.Kind), position);
        }
    }

    private static void RefreshTracking()
    {
        FakeGATManager? current = World.fakeGATManager;
        if (_tracker != current)
        {
            foreach (ushort local in Tracked) _tracker?.UnregisterHandle(GatInterop.ToHandle(local));
            Tracked.Clear();
            _tracker = current;
        }
        var wanted = new HashSet<ushort>();
        if (_identities != null && World.gat != null)
        {
            foreach (CommandMarker marker in _markers)
            {
                if (marker.Kind == CommandMarkerKind.Attack && _identities.TryGetLocal(marker.HostTarget, out ushort local) && World.gat.IsHandleValid(GatInterop.ToHandle(local))) wanted.Add(local);
            }
        }
        foreach (ushort local in Tracked.Where(local => !wanted.Contains(local)).ToArray())
        {
            _tracker?.UnregisterHandle(GatInterop.ToHandle(local));
            Tracked.Remove(local);
        }
        foreach (ushort local in wanted)
        {
            if (!Tracked.Contains(local) && _tracker != null && _tracker.RegisterHandle(GatInterop.ToHandle(local))) Tracked.Add(local);
        }
    }

    private static CommandVisuals.HudKind ToNative(CommandMarkerKind kind) => kind switch
    {
        CommandMarkerKind.Dropoff => CommandVisuals.HudKind.Dropoff,
        CommandMarkerKind.Goto => CommandVisuals.HudKind.Goto,
        CommandMarkerKind.Construction => CommandVisuals.HudKind.Construction,
        CommandMarkerKind.Attack => CommandVisuals.HudKind.Attack,
        CommandMarkerKind.Pickup => CommandVisuals.HudKind.Pickup,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
