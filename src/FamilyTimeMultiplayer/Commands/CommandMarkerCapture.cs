using System.Collections.Generic;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.Commands;

public static class CommandMarkerCapture
{
    public static CommandMarkersMessage Capture(FollowController follow, AttackCommandController attack, PickupWorkerScheduler pickup, ConstructionScheduler construction)
    {
        var markers = new List<CommandMarker>();
        if (follow.ShowFollowCursorHud) Add(markers, CommandMarkerKind.Goto, follow.FollowCursorTargetPosition);
        if (pickup.ActiveDropoffHudPosition is Vector3 dropoff) Add(markers, CommandMarkerKind.Dropoff, dropoff);
        if (construction.SourceHudPosition is Vector3 source) Add(markers, CommandMarkerKind.Construction, source);
        if (pickup.ActiveWaterCollectionTargetPosition is Vector3 water) Add(markers, CommandMarkerKind.Construction, water + Vector3.up * 1.5f);
        for (int i = 0; i < pickup.ActivePickupTargetCount; i++)
        {
            if (pickup.TryGetActivePickupTarget(i, out Vector3 position)) Add(markers, CommandMarkerKind.Pickup, position + Vector3.up * 1.5f);
        }
        for (int i = 0; i < attack.ActiveTargetCount; i++)
        {
            if (attack.TryGetActiveTarget(i, out Handle target) && World.gat != null && World.gat.IsHandleValid(target) &&
                World.fakeGATManager != null && World.fakeGATManager.TryGetPosition(target, out Vector3 position))
                markers.Add(new CommandMarker(CommandMarkerKind.Attack, GatInterop.ToNet(position + Vector3.up * 1.5f), target.stableIndex));
        }
        return new CommandMarkersMessage(markers);
    }

    private static void Add(List<CommandMarker> markers, CommandMarkerKind kind, Vector3 position) =>
        markers.Add(new CommandMarker(kind, GatInterop.ToNet(position)));
}
