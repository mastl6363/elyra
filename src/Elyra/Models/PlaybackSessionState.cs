namespace Elyra.Models;

public enum PlaybackRepeatMode
{
    Off,
    All,
    One
}

public sealed record PlaybackQueueItem(int QueueIndex, Track Track);

/// <summary>Serializable playback state restored without automatically starting audio.</summary>
public sealed class PlaybackSessionState
{
    public List<TrackSnapshot> Queue { get; set; } = [];
    public int CurrentIndex { get; set; } = -1;
    public long PositionMs { get; set; }
    public bool ShuffleEnabled { get; set; }
    public PlaybackRepeatMode RepeatMode { get; set; }
    public int Volume { get; set; } = 100;
}
