using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;
using UnityEngine;
using AnimationEvent = FamilyTimeMultiplayer.Core.Protocol.AnimationEvent;
using ProtocolAnimationEventKind = FamilyTimeMultiplayer.Core.Protocol.AnimationEventKind;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(GATAnimationManager), nameof(GATAnimationManager.PlayAnimations))]
internal static class AnimationPlayRecordPatch
{
    private static void Prefix(Handle[] handles, AnimationEnum animationType, int? motionSetFilterMatch, bool filterOnlyWhenIdling, AnimationEnum? filterOnlyAnimationType, Template templateHint)
    {
        if (AnimationRelay.Records && handles != null && handles.Length > 0)
        {
            int template = templateHint != null && World.gat != null ? World.gat.GetTemplateIndex(templateHint) : -1;
            AnimationRelay.Record(new AnimationEvent(ProtocolAnimationEventKind.Play, AnimationRelay.Stable(handles), (int)animationType, template,
                motionSetFilterMatch ?? -1, filterOnlyWhenIdling, filterOnlyAnimationType.HasValue ? (int)filterOnlyAnimationType.Value : -1, false, NetVector3.Zero, 0f));
        }
        AnimationRelay.Depth++;
    }

    private static void Finalizer() => AnimationRelay.Depth--;
}

[HarmonyPatch(typeof(GATAnimationManager), nameof(GATAnimationManager.PlayAnimationsSharedTemplate))]
internal static class AnimationSharedRecordPatch
{
    private static void Prefix(Handle[] handles, int templateIndex, int animationIndex, int? motionSetFilterMatch, bool filterOnlyWhenIdling, AnimationEnum? filterOnlyAnimationType)
    {
        if (AnimationRelay.Records && handles != null && handles.Length > 0)
        {
            AnimationRelay.Record(new AnimationEvent(ProtocolAnimationEventKind.PlayShared, AnimationRelay.Stable(handles), templateIndex, animationIndex,
                motionSetFilterMatch ?? -1, filterOnlyWhenIdling, filterOnlyAnimationType.HasValue ? (int)filterOnlyAnimationType.Value : -1, false, NetVector3.Zero, 0f));
        }
        AnimationRelay.Depth++;
    }

    private static void Finalizer() => AnimationRelay.Depth--;
}

[HarmonyPatch(typeof(GATAnimationManager), nameof(GATAnimationManager.PlayAnimationsWithinDistance))]
internal static class AnimationWithinDistanceRecordPatch
{
    private static void Prefix(Vector3 origin, float radius, AnimationEnum animationType, int templateIndex, int? motionSetFilterMatch, bool filterOnlyWhenIdling, AnimationEnum? filterOnlyAnimationType)
    {
        if (AnimationRelay.Records)
        {
            AnimationRelay.Record(new AnimationEvent(ProtocolAnimationEventKind.PlayWithinDistance, System.Array.Empty<ushort>(), (int)animationType, templateIndex,
                motionSetFilterMatch ?? -1, filterOnlyWhenIdling, filterOnlyAnimationType.HasValue ? (int)filterOnlyAnimationType.Value : -1, false, GatInterop.ToNet(origin), radius));
        }
        AnimationRelay.Depth++;
    }

    private static void Finalizer() => AnimationRelay.Depth--;
}

[HarmonyPatch(typeof(GATAnimationManager), nameof(GATAnimationManager.CompleteAnimations))]
internal static class AnimationCompleteRecordPatch
{
    private static void Prefix(Handle[] handles, AnimationEnum animationType, Template template)
    {
        if (AnimationRelay.Records && handles != null && handles.Length > 0 && template != null && World.gat != null)
        {
            AnimationRelay.Record(new AnimationEvent(ProtocolAnimationEventKind.Complete, AnimationRelay.Stable(handles), (int)animationType, World.gat.GetTemplateIndex(template),
                -1, false, -1, false, NetVector3.Zero, 0f));
        }
        AnimationRelay.Depth++;
    }

    private static void Finalizer() => AnimationRelay.Depth--;
}

[HarmonyPatch(typeof(GATAnimationManager), nameof(GATAnimationManager.SetInstanceMotionSets))]
internal static class AnimationMotionSetRecordPatch
{
    private static void Prefix(Handle[] handles, int motionSetIndex, int templateIndex, bool alsoAssignIdleAsAnimation)
    {
        if (AnimationRelay.Records && handles != null && handles.Length > 0)
        {
            AnimationRelay.Record(new AnimationEvent(ProtocolAnimationEventKind.MotionSets, AnimationRelay.Stable(handles), motionSetIndex, templateIndex,
                -1, false, -1, alsoAssignIdleAsAnimation, NetVector3.Zero, 0f));
        }
        AnimationRelay.Depth++;
    }

    private static void Finalizer() => AnimationRelay.Depth--;
}
