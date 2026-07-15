using Elyra.Services;

namespace Elyra.Tests;

public sealed class PlaybackOrderTests
{
    [Fact]
    public void Shuffle_PlaysEveryTrackOnceAndKeepsCurrentTrackFirst()
    {
        var order = new PlaybackOrder(new Random(42));
        order.Reset(6, 2);

        order.ToggleShuffle();

        var visited = new List<int> { order.CurrentIndex };
        while (order.TryMoveNext(out var index))
            visited.Add(index);

        Assert.Equal(2, visited[0]);
        Assert.Equal(Enumerable.Range(0, 6), visited.OrderBy(index => index));
        Assert.Equal(6, visited.Distinct().Count());
    }

    [Fact]
    public void Shuffle_PreviousFollowsTheVisitedOrder()
    {
        var order = new PlaybackOrder(new Random(42));
        order.Reset(5, 0);
        order.ToggleShuffle();
        Assert.True(order.TryMoveNext(out var firstNext));
        Assert.True(order.TryMoveNext(out _));

        Assert.True(order.TryMovePrevious(out var previous));

        Assert.Equal(firstNext, previous);
    }

    [Fact]
    public void DisablingShuffle_ContinuesInNaturalQueueOrder()
    {
        var order = new PlaybackOrder(new Random(42));
        order.Reset(5, 1);
        order.ToggleShuffle();
        order.ToggleShuffle();

        Assert.True(order.TryMoveNext(out var next));

        Assert.Equal(2, next);
    }

    [Fact]
    public void Reset_PreservesShuffleForANewQueue()
    {
        var order = new PlaybackOrder(new Random(42));
        order.Reset(3, 0);
        order.ToggleShuffle();

        order.Reset(4, 3);

        Assert.True(order.ShuffleEnabled);
        Assert.Equal(3, order.CurrentIndex);
        var remaining = new List<int>();
        while (order.TryMoveNext(out var next))
            remaining.Add(next);
        Assert.Equal([0, 1, 2], remaining.OrderBy(index => index));
    }
}
