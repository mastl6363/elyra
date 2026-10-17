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

    [Fact]
    public void Shuffle_InsertedTrackReallyPlaysNext()
    {
        var order = new PlaybackOrder(new Random(42));
        order.Reset(4, 1);
        order.ToggleShuffle();

        order.AppendNextInShuffle();

        Assert.Equal(4, order.UpcomingIndices[0]);
        Assert.True(order.TryMoveNext(out var next));
        Assert.Equal(4, next);
    }

    [Fact]
    public void Shuffle_RemovingEarlierQueueIndexKeepsCurrentTrack()
    {
        var order = new PlaybackOrder(new Random(42));
        order.Reset(4, 3);
        order.ToggleShuffle();
        var removed = order.UpcomingIndices.ToList();
        var position = removed.FindIndex(index => index < 3);

        var removedIndex = order.RemoveUpcomingAt(position);

        Assert.True(removedIndex < 3);
        Assert.Equal(2, order.CurrentIndex);
        Assert.Equal(2, order.UpcomingIndices.Count);
        Assert.Equal(
            removed.Where(index => index != removedIndex)
                .Select(index => index > removedIndex ? index - 1 : index)
                .OrderBy(index => index),
            order.UpcomingIndices.OrderBy(index => index));
    }

    [Fact]
    public void Shuffle_MovingUpcomingChangesPlaybackOrder()
    {
        var order = new PlaybackOrder(new Random(42));
        order.Reset(4, 0);
        order.ToggleShuffle();
        var original = order.UpcomingIndices.ToList();

        Assert.True(order.MoveUpcoming(1, -1));

        Assert.Equal(original[1], order.UpcomingIndices[0]);
        Assert.Equal(original[0], order.UpcomingIndices[1]);
    }
}
