using System.Collections.Generic;
using FamilyTimeMultiplayer.Core.Commands;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public class FollowShareBalancerTests
{
    [Fact]
    public void SecondPlayerTakesHalfWhenFirstHoldsEveryone()
    {
        var take = FollowShareBalancer.TakeForRequester(requesterId: 2, freeWorkers: 0, new Dictionary<int, int> { [1] = 6 });
        Assert.Equal(3, take[1]);
    }

    [Fact]
    public void OddCountLeavesExtraWithCurrentHolder()
    {
        var take = FollowShareBalancer.TakeForRequester(2, 0, new Dictionary<int, int> { [1] = 3 });
        Assert.Equal(1, take[1]);
    }

    [Fact]
    public void FreeWorkersCoverTheShareWithoutTakingAnything()
    {
        var take = FollowShareBalancer.TakeForRequester(2, 4, new Dictionary<int, int> { [1] = 4 });
        Assert.Empty(take);
    }

    [Fact]
    public void ThreePlayersTakeFromTheLargestGroupFirst()
    {
        var take = FollowShareBalancer.TakeForRequester(3, 0, new Dictionary<int, int> { [1] = 5, [2] = 1 });
        Assert.Equal(new Dictionary<int, int> { [1] = 2 }, take);
    }

    [Fact]
    public void SingleKidIsNotTakenFromItsHolder()
    {
        var take = FollowShareBalancer.TakeForRequester(2, 0, new Dictionary<int, int> { [1] = 1 });
        Assert.Empty(take);
    }
}
