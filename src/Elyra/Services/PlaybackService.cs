using Elyra.Models;

namespace Elyra.Services;

/// <summary>
/// High-level playback: owns the play queue and drives <see cref="AudioPlayerService"/>.
/// The UI talks to this; it never touches LibVLC directly. Registered as a singleton.
/// </summary>
public sealed class PlaybackService
{
    private readonly AudioPlayerService _audio;
    private readonly PlaybackOrder _order = new();
    private List<Track> _queue = new();

    public PlaybackService(AudioPlayerService audio)
    {
        _audio = audio;
        // EndReached fires on a LibVLC callback thread — never call back into LibVLC
        // from there (it can deadlock), so hop onto the thread pool before advancing.
        _audio.TrackEnded += (_, _) => Task.Run(Next);
    }

    public IReadOnlyList<Track> Queue => _queue;
    public int CurrentIndex => _order.CurrentIndex;
    public Track? Current => CurrentIndex >= 0 && CurrentIndex < _queue.Count ? _queue[CurrentIndex] : null;
    public RadioStation? CurrentStation { get; private set; }
    public bool HasCurrent => Current is not null || CurrentStation is not null;
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
        CurrentStation = null;
        _queue = tracks.ToList();
        _order.Reset(_queue.Count, startIndex);
        if (_queue.Count == 0)
        {
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        StartCurrentTrack();
    }

    public void PlayRadio(RadioStation station)
    {
        _queue = [];
        _order.Reset(0, 0);
        CurrentStation = station;
        _audio.PlayLocation(station.StreamUrl);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StartCurrentTrack()
    {
        if (CurrentIndex < 0 || CurrentIndex >= _queue.Count) return;
        _audio.Play(_queue[CurrentIndex].FilePath);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleShuffle()
    {
        _order.ToggleShuffle();
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePlayPause()
    {
        if (HasCurrent)
            _audio.TogglePlayPause();
    }

    public void Next()
    {
        if (CurrentStation is not null) return;
        if (_order.TryMoveNext(out _))
            StartCurrentTrack();
        else
            _audio.Stop();
    }

    public void Previous()
    {
        if (CurrentStation is not null) return;
        // Restart the current track if we're more than 3s in (or it's the first one),
        // otherwise step back to the previous track.
        if (_audio.Position.TotalSeconds > 3)
            _audio.Seek(TimeSpan.Zero);
        else if (_order.TryMovePrevious(out _))
            StartCurrentTrack();
        else
            _audio.Seek(TimeSpan.Zero);
    }

    public void Seek(TimeSpan position) => _audio.Seek(position);
}
