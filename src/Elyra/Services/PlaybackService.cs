using Elyra.Models;

namespace Elyra.Services;

/// <summary>
/// High-level playback: owns the play queue and drives <see cref="AudioPlayerService"/>.
/// The UI talks to this; it never touches LibVLC directly. Registered as a singleton.
/// </summary>
public sealed class PlaybackService
{
    // Guards _queue/_order: Next()/Previous() can be called from the UI thread
    // (button click) at the same time TrackEnded fires and hops onto a thread-pool
    // thread, and both paths read-then-mutate PlaybackOrder's internal state.
    private readonly object _sync = new();
    private readonly AudioPlayerService _audio;
    private readonly PlayHistoryService _history;
    private readonly NextcloudMusicService _nextcloud;
    private readonly PlaybackOrder _order = new();
    private List<Track> _queue = new();

    public PlaybackService(AudioPlayerService audio, PlayHistoryService history, NextcloudMusicService nextcloud)
    {
        _audio = audio;
        _history = history;
        _nextcloud = nextcloud;
        // Keep track advancement independent of event delivery; a player event
        // must never call back into LibVLC synchronously.
        _audio.TrackEnded += (_, _) => Task.Run(Next);
        // A missing/deleted file (e.g. a stale playlist entry) must not leave
        // "now playing" stuck forever — skip it like a track that ended.
        _audio.PlaybackFailed += (_, _) => Task.Run(Next);
    }

    public IReadOnlyList<Track> Queue => _queue;
    public IReadOnlyList<Track> Upcoming
    {
        get
        {
            lock (_sync)
                return _order.UpcomingIndices.Select(index => _queue[index]).ToList();
        }
    }
    public int CurrentIndex => _order.CurrentIndex;
    public Track? Current => CurrentIndex >= 0 && CurrentIndex < _queue.Count ? _queue[CurrentIndex] : null;
    public RadioStation? CurrentStation { get; private set; }
    public VideoItem? CurrentVideo { get; private set; }
    public bool HasCurrent => Current is not null || CurrentStation is not null || CurrentVideo is not null;
    public bool ShuffleEnabled => _order.ShuffleEnabled;

    public bool IsPlaying => _audio.IsPlaying;
    public string? PlaybackError { get; private set; }
    public TimeSpan Position => _audio.Position;
    public TimeSpan Duration => _audio.Duration;

    public int Volume
    {
        get => _audio.Volume;
        set => _audio.Volume = value;
    }

    /// <summary>Raised when the current track changes (for the UI to refresh).</summary>
    public event EventHandler? CurrentChanged;

    public void Play(IEnumerable<Track> tracks, int startIndex = 0)
    {
        bool hasTracks;
        lock (_sync)
        {
            CurrentStation = null;
            CurrentVideo = null;
            _queue = tracks.ToList();
            _order.Reset(_queue.Count, startIndex);
            hasTracks = _queue.Count > 0;
        }

        if (!hasTracks)
        {
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        StartCurrentTrack();
    }

    public void PlayRadio(RadioStation station)
    {
        PlaybackError = null;
        lock (_sync)
        {
            _queue = [];
            _order.Reset(0, 0);
            CurrentStation = station;
            CurrentVideo = null;
        }
        _audio.PlayLocation(station.StreamUrl);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PlayVideo(VideoItem video)
    {
        PlaybackError = null;
        lock (_sync)
        {
            _queue = [];
            _order.Reset(0, 0);
            CurrentStation = null;
            CurrentVideo = video;
        }
        _audio.PlayVideo(video);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StopVideo()
    {
        if (CurrentVideo is null) return;
        _audio.Stop();
        CurrentVideo = null;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StartCurrentTrack()
    {
        Track track;
        lock (_sync)
        {
            if (CurrentIndex < 0 || CurrentIndex >= _queue.Count) return;
            track = _queue[CurrentIndex];
        }
        try
        {
            PlaybackError = null;
            if (track.NextcloudPath is null) _audio.Play(track.FilePath);
            else
            {
                var location = _nextcloud.GetPlaybackLocation(track);
                if (location.StartsWith("http://127.0.0.1:", StringComparison.Ordinal)) _audio.PlayLocation(location);
                else _audio.Play(location);
            }
            _history.RecordPlay(track.FilePath);
        }
        catch (Exception ex)
        {
            _audio.Stop();
            PlaybackError = ex.Message;
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleShuffle()
    {
        lock (_sync) _order.ToggleShuffle();
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PlayNext(Track track)
    {
        if (Current is null)
        {
            Play([track]);
            return;
        }

        lock (_sync)
        {
            var queue = _queue.ToList();
            if (_order.ShuffleEnabled)
            {
                queue.Add(track);
                _order.AppendNextInShuffle();
            }
            else
            {
                queue.Insert(_order.CurrentIndex + 1, track);
                _order.Reset(queue.Count, _order.CurrentIndex);
            }
            _queue = queue;
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveUpcomingAt(int position)
    {
        lock (_sync)
        {
            if ((uint)position >= (uint)_order.UpcomingIndices.Count) return;
            var queue = _queue.ToList();
            queue.RemoveAt(_order.RemoveUpcomingAt(position));
            _queue = queue;
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MoveUpcoming(int position, int direction)
    {
        lock (_sync)
        {
            var upcoming = _order.UpcomingIndices;
            var next = position + direction;
            if ((uint)position >= (uint)upcoming.Count || (uint)next >= (uint)upcoming.Count) return;

            if (_order.ShuffleEnabled)
                _order.MoveUpcoming(position, direction);
            else
            {
                var queue = _queue.ToList();
                (queue[upcoming[position]], queue[upcoming[next]]) =
                    (queue[upcoming[next]], queue[upcoming[position]]);
                _queue = queue;
            }
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePlayPause()
    {
        if (HasCurrent)
            _audio.TogglePlayPause();
    }

    public void Next()
    {
        bool moved;
        lock (_sync)
        {
            if (CurrentStation is not null || CurrentVideo is not null) return;
            moved = _order.TryMoveNext(out _);
        }

        if (moved)
            StartCurrentTrack();
        else
            _audio.Stop();
    }

    public void Previous()
    {
        lock (_sync)
            if (CurrentStation is not null || CurrentVideo is not null) return;

        // Restart the current track if we're more than 3s in (or it's the first one),
        // otherwise step back to the previous track.
        if (_audio.Position.TotalSeconds > 3)
        {
            _audio.Seek(TimeSpan.Zero);
            return;
        }

        bool moved;
        lock (_sync) moved = _order.TryMovePrevious(out _);

        if (moved)
            StartCurrentTrack();
        else
            _audio.Seek(TimeSpan.Zero);
    }

    public void Seek(TimeSpan position) => _audio.Seek(position);
}
