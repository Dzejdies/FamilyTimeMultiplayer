using System.Reflection;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Commands;

public interface IClientCommandSink
{
    void SendIntent(CommandIntentMessage intent);

    bool TryGetRigidNetId(RigidTransform rigid, out int netId);

    bool TryGetHostAgent(ushort localStableIndex, out ushort hostStableIndex);
}

public static class ClientCommandBridge
{
    private static readonly MethodInfo RefreshCrosshair =
        AccessTools.Method(typeof(CommandService), "RefreshCrosshairIndicators")
        ?? throw new System.MissingMethodException(nameof(CommandService), "RefreshCrosshairIndicators");

    private static IClientCommandSink? _sink;

    public static bool IsActive => _sink != null;

    public static int? HostAvailableWorkers { get; private set; }

    public static int? HostTotalWorkers { get; private set; }

    public static void Attach(IClientCommandSink sink)
    {
        _sink = sink;
        HostAvailableWorkers = null;
        HostTotalWorkers = null;
    }

    public static void Detach()
    {
        ClientCommandOutlines.Clear();
        ClientCommandMarkers.Clear();
        _sink = null;
        HostAvailableWorkers = null;
        HostTotalWorkers = null;
        UI.PlayerNotice.Clear();
        RefreshLocalCrosshair();
    }

    public static void Send(CommandIntentMessage intent) => _sink?.SendIntent(intent);

    internal static bool TryGetHostAgent(ushort localStableIndex, out ushort hostStableIndex)
    {
        hostStableIndex = ushort.MaxValue;
        return _sink != null && _sink.TryGetHostAgent(localStableIndex, out hostStableIndex);
    }

    internal static bool TryGetNetId(RigidTransform rigid, out int netId)
    {
        netId = -1;
        return _sink != null && _sink.TryGetRigidNetId(rigid, out netId);
    }

    public static void ApplyFeedback(CommandFeedbackMessage feedback)
    {
        bool changed = HostAvailableWorkers != feedback.AvailableWorkers || HostTotalWorkers != feedback.TotalWorkers;
        HostAvailableWorkers = feedback.AvailableWorkers;
        HostTotalWorkers = feedback.TotalWorkers;
        if (feedback.Outcome == CommandOutcome.Refused)
        {
            ShowNotice($"Refused: {feedback.Detail}");
        }
        else if (feedback.Outcome == CommandOutcome.Accepted && (feedback.Detail == "locked" || feedback.Detail == "unlocked" || feedback.Detail.StartsWith("kids go", System.StringComparison.Ordinal)))
        {
            ShowNotice(char.ToUpperInvariant(feedback.Detail[0]) + feedback.Detail.Substring(1));
        }
        if (changed)
        {
            RefreshLocalCrosshair();
        }
    }

    public static void ShowNotice(string text) => UI.PlayerNotice.Show(text);

    public static CommandIntentMessage? ResolveInteractTarget(CommandService service)
    {
        if (_sink == null)
        {
            return null;
        }
        CommandVisuals? visuals = Traverse.Create(service).Field<CommandVisuals>("_visuals").Value;
        AttackCommandController? attack = Traverse.Create(service).Field<AttackCommandController>("_attack").Value;
        if (attack != null && World.gatCommandManager?.GetCommand<GATAttackBehavior>() != null
            && attack.TryGetAttackCommandTarget(out Handle attackTarget, out Vector3 hint)
            && _sink.TryGetHostAgent(attackTarget.stableIndex, out ushort hostTarget))
        {
            return CommandIntentMessage.InteractAgent(hostTarget, GatInterop.ToNet(hint));
        }
        Camera? camera = Camera.main;
        float planReach = GameApiCompatibility.CommandRaycastDistance(service, 30f);
        if (camera != null && World.buildPlanManager != null && World.buildPartManager != null
            && World.buildPlanManager.GetBuildPlan(camera.transform.position, camera.transform.forward, planReach, out BuildPlanManager.BuildPlan plan))
        {
            BuildPart part = World.buildPartManager.BuildPartTemplates[plan.templateIndex];
            Matrix4x4 placement = part.GetPlacementMatrix(plan.gridPosition, plan.rotation);
            return CommandIntentMessage.InteractBuildPlan(GatInterop.ToNet(placement.MultiplyPoint3x4(part.GetCombinedMeshBounds().center)));
        }
        if (visuals != null && visuals.HoveredBoxId.HasValue && World.box3DManager != null && World.box3DManager.TryGetStation(visuals.HoveredBoxId.Value, out GATStation station))
        {
            return CommandIntentMessage.InteractStation(GatInterop.ToNet(station.Position));
        }
        if (visuals != null && visuals.HoveredRigidId.HasValue)
        {
            RigidTransform rigid = visuals.HoveredRigid;
            if ((rigid != null || RigidTransform.TryGetById(visuals.HoveredRigidId.Value, out rigid)) && _sink.TryGetRigidNetId(rigid, out int netId))
            {
                return CommandIntentMessage.InteractRigid(netId);
            }
        }
        HandService? hand = PlayerController.hand;
        if (hand == null)
        {
            return null;
        }
        float reach = GameApiCompatibility.CommandRaycastDistance(service, 30f);
        if (hand.TryGetHoveredPickupProp(reach, out PropChunkManager.PropCollisionInstance prop, out _) && prop.prop != null)
        {
            return CommandIntentMessage.InteractProp(prop.worldX, prop.worldZ, prop.prop.name);
        }
        if (hand.TryGetLastWorldHit(out World.HitInfo hit))
        {
            return CommandIntentMessage.InteractGround(GatInterop.ToNet(hit.point));
        }
        return null;
    }

    public static CommandIntentMessage? ResolveCommandLockTarget()
    {
        if (_sink == null || World.commandOwnershipService == null)
        {
            return null;
        }
        HandService? hand = PlayerController.hand;
        CommandOwnershipService ownership = World.commandOwnershipService;
        if (hand != null && hand.TryGetDistantHoverHandle(out Handle target, out _, out _, candidate => ownership.IsCommandLockCandidate(candidate.Handle))
            && _sink.TryGetHostAgent(target.stableIndex, out ushort hostTarget))
        {
            return CommandIntentMessage.ToggleCommandLock(hostTarget);
        }
        return null;
    }

    public static CommandIntentMessage? ResolvePickupSweepTarget(CommandService service)
    {
        if (_sink == null) return null;
        CommandVisuals? visuals = Traverse.Create(service).Field<CommandVisuals>("_visuals").Value;
        if (visuals == null || !visuals.HoveredRigidId.HasValue) return null;
        RigidTransform rigid = visuals.HoveredRigid;
        if (rigid == null || rigid.CommandShape == GATPickupBehavior.CommandShape.NONE || !_sink.TryGetRigidNetId(rigid, out int netId)) return null;
        return CommandIntentMessage.PickupSweep(netId, started: true);
    }

    private static void RefreshLocalCrosshair()
    {
        if (World.commandService != null)
        {
            RefreshCrosshair.Invoke(World.commandService, null);
        }
    }
}
