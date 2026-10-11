using System.IO;
using FamilyTimeMultiplayer.Core.Protocol;

namespace FamilyTimeMultiplayer.Core.Safety;

public static class AddonCommandPose
{
    public static ClientPoseMessage Compose(ClientPoseMessage canonical, ClientPoseMessage? authenticatedLook)
    {
        if (authenticatedLook == null) return canonical;
        if (!MotionBudget.ValidPose(authenticatedLook)) throw new InvalidDataException("Invalid authenticated addon command look.");
        return new ClientPoseMessage(canonical.Position, authenticatedLook.YawDegrees, authenticatedLook.PitchDegrees, canonical.Velocity);
    }
}
