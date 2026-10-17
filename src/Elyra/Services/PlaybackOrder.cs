namespace Elyra.Services;

/// <summary>
/// Tracks the current queue position in either its natural or a shuffled order.
/// Keeping this separate from the audio engine makes the navigation deterministic
/// and independently testable.
/// </summary>
public sealed class PlaybackOrder
{
    private readonly Random _random;
    private readonly List<int> _shuffleOrder = [];
    private int _shufflePosition = -1;
    private int _count;

    public PlaybackOrder(Random? random = null) => _random = random ?? Random.Shared;

    public bool ShuffleEnabled { get; private set; }
    public int CurrentIndex { get; private set; } = -1;

    /// <summary>Queue indices in the order they will actually play.</summary>
    public IReadOnlyList<int> UpcomingIndices => CurrentIndex < 0 ? [] : ShuffleEnabled
        ? _shuffleOrder.Skip(_shufflePosition + 1).ToArray()
        : Enumerable.Range(CurrentIndex + 1, _count - CurrentIndex - 1).ToArray();

    public void Reset(int count, int startIndex)
    {
        _count = Math.Max(0, count);
        CurrentIndex = _count == 0 ? -1 : Math.Clamp(startIndex, 0, _count - 1);
        RebuildShuffleOrder();
    }

    public void ToggleShuffle()
    {
        ShuffleEnabled = !ShuffleEnabled;
        RebuildShuffleOrder();
    }

    public bool TryMoveNext(out int index)
    {
        if (ShuffleEnabled)
        {
            if (_shufflePosition + 1 >= _shuffleOrder.Count)
                return NoIndex(out index);

            CurrentIndex = _shuffleOrder[++_shufflePosition];
        }
        else
        {
            if (CurrentIndex + 1 >= _count)
                return NoIndex(out index);

            CurrentIndex++;
        }

        index = CurrentIndex;
        return true;
    }

    public bool TryMovePrevious(out int index)
    {
        if (ShuffleEnabled)
        {
            if (_shufflePosition <= 0)
                return NoIndex(out index);

            CurrentIndex = _shuffleOrder[--_shufflePosition];
        }
        else
        {
            if (CurrentIndex <= 0)
                return NoIndex(out index);

            CurrentIndex--;
        }

        index = CurrentIndex;
        return true;
    }

    /// <summary>Append a new queue item, but play it directly after the current shuffled item.</summary>
    public void AppendNextInShuffle()
    {
        if (!ShuffleEnabled || CurrentIndex < 0)
            throw new InvalidOperationException("A shuffled track must be playing.");

        _shuffleOrder.Insert(_shufflePosition + 1, _count);
        _count++;
    }

    /// <summary>Remove an upcoming item and return its index in the source queue.</summary>
    public int RemoveUpcomingAt(int position)
    {
        var upcoming = UpcomingIndices;
        if ((uint)position >= (uint)upcoming.Count)
            throw new ArgumentOutOfRangeException(nameof(position));

        var index = upcoming[position];
        _count--;
        if (ShuffleEnabled)
        {
            _shuffleOrder.RemoveAt(_shufflePosition + 1 + position);
            for (var i = 0; i < _shuffleOrder.Count; i++)
                if (_shuffleOrder[i] > index) _shuffleOrder[i]--;
            if (CurrentIndex > index) CurrentIndex--;
        }

        return index;
    }

    /// <summary>Move an upcoming shuffled item one slot. Natural queue moves are handled by the owner.</summary>
    public bool MoveUpcoming(int position, int direction)
    {
        var next = position + direction;
        var count = UpcomingIndices.Count;
        if ((uint)position >= (uint)count || (uint)next >= (uint)count)
            return false;

        if (ShuffleEnabled)
        {
            var first = _shufflePosition + 1 + position;
            var second = _shufflePosition + 1 + next;
            (_shuffleOrder[first], _shuffleOrder[second]) = (_shuffleOrder[second], _shuffleOrder[first]);
        }
        return true;
    }

    private void RebuildShuffleOrder()
    {
        _shuffleOrder.Clear();
        _shufflePosition = -1;

        if (!ShuffleEnabled || CurrentIndex < 0)
            return;

        _shuffleOrder.Add(CurrentIndex);
        var remaining = Enumerable.Range(0, _count).Where(index => index != CurrentIndex).ToList();
        for (var i = remaining.Count - 1; i > 0; i--)
        {
            var swapIndex = _random.Next(i + 1);
            (remaining[i], remaining[swapIndex]) = (remaining[swapIndex], remaining[i]);
        }

        _shuffleOrder.AddRange(remaining);
        _shufflePosition = 0;
    }

    private static bool NoIndex(out int index)
    {
        index = -1;
        return false;
    }
}
