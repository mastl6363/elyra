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
    private readonly PlaybackOrder _order = new();
    private List<Track> _queue = new();

    public PlaybackService(AudioPlayerService audio)
    {
        _audio = audio;
        // EndReached fires on a LibVLC callback thread — never call back into LibVLC
        // from there (it can deadlock), so hop onto the thread pool before advancing.
        _audio.TrackEnded += (_, _) => Task.Run(Next);
        // A missing/deleted file (e.g. a stale playlist entry) must not leave
        // "now playing" stuck forever — skip it like a track that ended.
        _audio.PlaybackFailed += (_, _) => Task.Run(Next);
    }

    public IReadOnlyList<Track> Queue => _queue;
    public int CurrentIndex => _order.CurrentIndex;
    public Track? Current => CurrentIndex >= 0 && CurrentIndex < _queue.Count ? _queue[CurrentIndex] : null;
    public RadioStation? CurrentStation { get; private set; }
    public VideoItem? CurrentVideo { get; private set; }
    public bool HasCurrent => Current is not null || CurrentStation is not null || CurrentVideo is not null;
    public bool ShuffleEnabled => _order.ShuffleEnabled;

    public bool IsPlaying => _audio.IsPlaying;
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
        string? filePath;
        lock (_sync)
        {
            if (CurrentIndex < 0 || CurrentIndex >= _queue.Count) return;
            filePath = _queue[CurrentIndex].FilePath;
        }
        _audio.Play(filePath);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleShuffle()
    {
        lock (_sync) _order.ToggleShuffle();
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
