using Elyra.Models;

namespace Elyra.Services;

/// <summary>
/// High-level playback: owns the play queue and drives <see cref="AudioPlayerService"/>.
/// The UI talks to this; it never touches LibVLC directly. Registered as a singleton.
/// </summary>
public sealed class PlaybackService
{
    private readonly AudioPlayerService _audio;
    private List<Track> _queue = new();

    public PlaybackService(AudioPlayerService audio)
    {
        _audio = audio;
        // EndReached fires on a LibVLC callback thread — never call back into LibVLC
        // from there (it can deadlock), so hop onto the thread pool before advancing.
        _audio.TrackEnded += (_, _) => Task.Run(Next);
    }

    public IReadOnlyList<Track> Queue => _queue;
    public int CurrentIndex { get; private set; } = -1;
    public Track? Current => CurrentIndex >= 0 && CurrentIndex < _queue.Count ? _queue[CurrentIndex] : null;

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
        _queue = tracks.ToList();
        if (_queue.Count == 0)
        {
            CurrentIndex = -1;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        PlayAt(Math.Clamp(startIndex, 0, _queue.Count - 1));
    }

    public void PlayAt(int index)
    {
        if (index < 0 || index >= _queue.Count) return;
        CurrentIndex = index;
        _audio.Play(_queue[index].FilePath);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePlayPause()
    {
        if (Current is not null)
            _audio.TogglePlayPause();
    }

    public void Next()
    {
        if (CurrentIndex + 1 < _queue.Count)
            PlayAt(CurrentIndex + 1);
        else
            _audio.Stop();
    }

    public void Previous()
    {
        // Restart the current track if we're more than 3s in (or it's the first one),
        // otherwise step back to the previous track.
        if (_audio.Position.TotalSeconds > 3 || CurrentIndex <= 0)
            _audio.Seek(TimeSpan.Zero);
        else
            PlayAt(CurrentIndex - 1);
    }

    public void Seek(TimeSpan position) => _audio.Seek(position);
}
