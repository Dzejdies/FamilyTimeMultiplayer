using System.Collections.Generic;
using FamilyTimeMultiplayer.Core.Protocol;

namespace FamilyTimeMultiplayer.Core.Addons;

public interface INativeActionPolicy
{
    bool CanPerformNativeAction(int playerId, INetMessage message);
}

public static class NativeActionPolicy
{
    public static bool Allows(int playerId, INetMessage message, IEnumerable<INativeActionPolicy> policies)
    {
        if (message is CommandIntentMessage intent && intent.Kind == CommandIntentKind.PickupSweepStopped) return true;
        if (!RequiresPermission(message)) return true;
        foreach (INativeActionPolicy policy in policies)
            if (!policy.CanPerformNativeAction(playerId, message)) return false;
        return true;
    }

    private static bool RequiresPermission(INetMessage message) =>
        message is CommandIntentMessage or ToolEditsMessage or MamaToggleMessage or BedRequestMessage ||
        message is HoldMessage hold && hold.Phase == HoldPhase.Begin;
}
