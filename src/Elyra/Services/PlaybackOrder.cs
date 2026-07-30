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

    public void Reset(int count, int startIndex)
    {
        _count = Math.Max(0, count);
        CurrentIndex = _count == 0 ? -1 : Math.Clamp(startIndex, 0, _count - 1);
        RebuildShuffleOrder();
    }

    public void ToggleShuffle()
    {
        SetShuffle(!ShuffleEnabled);
    }

    public void SetShuffle(bool enabled)
    {
        if (ShuffleEnabled == enabled)
            return;
        ShuffleEnabled = enabled;
        RebuildShuffleOrder();
    }

    /// <summary>Makes an explicitly inserted queue item the next shuffled item.</summary>
    public void PrioritizeNext(int index)
    {
        if (!ShuffleEnabled || index < 0 || index >= _count)
            return;

        _shuffleOrder.Remove(index);
        _shuffleOrder.Insert(Math.Min(_shufflePosition + 1, _shuffleOrder.Count), index);
    }

    public IReadOnlyList<int> UpcomingIndices() => ShuffleEnabled
        ? _shuffleOrder.Skip(_shufflePosition + 1).ToList()
        : Enumerable.Range(Math.Max(0, CurrentIndex + 1), Math.Max(0, _count - CurrentIndex - 1)).ToList();

    public bool TryPeekNext(out int index)
    {
        if (ShuffleEnabled)
        {
            if (_shufflePosition + 1 >= _shuffleOrder.Count)
                return NoIndex(out index);
            index = _shuffleOrder[_shufflePosition + 1];
            return true;
        }

        if (CurrentIndex + 1 >= _count)
            return NoIndex(out index);
        index = CurrentIndex + 1;
        return true;
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
