using System.Collections;
using System.Collections.Generic;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Commands;

public static class FollowGroups
{
    public static List<Handle> ReleasableFollowers(FollowController follow, PickupWorkerScheduler pickup)
    {
        var handles = new List<Handle>();
        if (!follow.IsFollowing)
        {
            return handles;
        }
        IList sessions = Traverse.Create(follow).Field<IList>("_followSessions").Value
            ?? throw new System.MissingFieldException(nameof(FollowController), "_followSessions");
        foreach (object session in sessions)
        {
            Traverse fields = Traverse.Create(session);
            CommandAgentGroup? group = fields.Field<CommandAgentGroup>("group").Value;
            CommandAgentGroup? transferSource = fields.Field<CommandAgentGroup>("transferSource").Value;
            if (group == null || GameApiCompatibility.IsFollowingPickupGroup(pickup, transferSource))
            {
                continue;
            }
            handles.AddRange(group.GetAgentSnapshot());
        }
        return handles;
    }

    public static int Release(FollowController follow, PickupWorkerScheduler pickup, int count)
    {
        List<Handle> followers = ReleasableFollowers(follow, pickup);
        int released = 0;
        for (int i = followers.Count - 1; i >= 0 && released < count; i--)
        {
            follow.StopFollowingAgent(followers[i]);
            released++;
        }
        return released;
    }
}
