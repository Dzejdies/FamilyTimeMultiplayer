using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyTimeMultiplayer.Core.Commands;

public static class FollowShareBalancer
{
    public static IReadOnlyDictionary<int, int> TakeForRequester(int requesterId, int freeWorkers, IReadOnlyDictionary<int, int> followersByPlayer)
    {
        if (freeWorkers < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(freeWorkers), "Free worker count cannot be negative.");
        }
        var others = followersByPlayer.Where(entry => entry.Key != requesterId && entry.Value > 0).ToDictionary(entry => entry.Key, entry => entry.Value);
        int requesterOwned = followersByPlayer.TryGetValue(requesterId, out int owned) ? owned : 0;
        int pool = freeWorkers + requesterOwned + others.Values.Sum();
        int share = pool / (others.Count + 1);
        int stillNeeded = share - requesterOwned - freeWorkers;
        var take = new Dictionary<int, int>();
        if (stillNeeded <= 0)
        {
            return take;
        }
        foreach (KeyValuePair<int, int> donor in others.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key))
        {
            int surplus = donor.Value - share;
            if (surplus <= 0)
            {
                continue;
            }
            int amount = Math.Min(surplus, stillNeeded);
            take[donor.Key] = amount;
            stillNeeded -= amount;
            if (stillNeeded == 0)
            {
                break;
            }
        }
        return take;
    }
}
