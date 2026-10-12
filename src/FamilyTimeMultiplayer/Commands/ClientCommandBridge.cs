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
    internal static bool CanForwardNativeAction => IsActive && ReplicationGate.BlocksLocalSimulation;

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
        ClearNativeGestures();
        ClientCommandOutlines.Clear();
        ClientCommandMarkers.Clear();
        _sink = null;
        HostAvailableWorkers = null;
        HostTotalWorkers = null;
        UI.PlayerNotice.Clear();
        RefreshLocalCrosshair();
    }

    public static void Send(CommandIntentMessage intent)
    {
        if (CanForwardNativeAction) _sink!.SendIntent(intent);
    }

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

    private static void ClearNativeGestures()
    {
        CommandService? service = World.commandService;
        if (service == null || World.isShuttingDown) return;
        using var nativeCleanup = ReplicationGate.AuthorizeReplication();
        GameApiCompatibility.ResetLocalCommandGestures(service);
    }

    private static void RefreshLocalCrosshair()
    {
        if (World.commandService != null)
        {
            RefreshCrosshair.Invoke(World.commandService, null);
        }
    }
}
