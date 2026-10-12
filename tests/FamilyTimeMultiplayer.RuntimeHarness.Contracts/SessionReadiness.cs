namespace FamilyTimeMultiplayer.RuntimeHarness.Contracts;

public static class SessionReadiness
{
    public static bool HasLivePair(bool isHost, bool sessionIsLive, int rosterCount, int liveRemotePlayerCount)
    {
        if (rosterCount < 2) return false;
        return isHost ? liveRemotePlayerCount > 0 : sessionIsLive;
    }
}
