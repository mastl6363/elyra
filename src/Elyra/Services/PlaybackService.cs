using Elyra.Models;

namespace Elyra.Services;

/// <summary>
/// High-level playback: owns the play queue and drives <see cref="AudioPlayerService"/>.
/// The UI talks to this; it never touches LibVLC directly. Registered as a singleton.
/// </summary>
public sealed class PlaybackService
{
    private const int MaxConsecutiveFailures = 3;
    // Queue updates and native player commands must stay in the same order.
    // Native completion callbacks reach this lock on the thread pool.
    private readonly object _sync = new();
    private readonly AudioPlayerService _audio;
    private readonly PlayHistoryService _history;
    private readonly NextcloudMusicService _nextcloud;
    private readonly PlaybackOrder _order = new();
    private IReadOnlyList<Track> _queue = [];
    private RadioStation? _station;
    private VideoItem? _video;
    private string? _error;
    private int _consecutiveFailures;
    private bool _audioPlaybackRequested;
    private bool _canResumePausedInput;

    public PlaybackService(AudioPlayerService audio, PlayHistoryService history, NextcloudMusicService nextcloud)
    {
        _audio = audio;
        _history = history;
        _nextcloud = nextcloud;
        _audio.PlaybackCompleted += OnPlaybackCompleted;
    }

    public IReadOnlyList<Track> Queue { get { lock (_sync) return _queue; } }
    public IReadOnlyList<Track> Upcoming
    {
        get
        {
            lock (_sync)
                return _order.UpcomingIndices.Select(index => _queue[index]).ToList();
        }
    }
    public int CurrentIndex { get { lock (_sync) return _order.CurrentIndex; } }
    public Track? Current { get { lock (_sync) return CurrentTrack; } }
    public RadioStation? CurrentStation { get { lock (_sync) return _station; } }
    public VideoItem? CurrentVideo { get { lock (_sync) return _video; } }
    public bool HasCurrent { get { lock (_sync) return CurrentTrack is not null || _station is not null || _video is not null; } }
    public bool ShuffleEnabled { get { lock (_sync) return _order.ShuffleEnabled; } }
    private Track? CurrentTrack => _order.CurrentIndex >= 0 ? _queue[_order.CurrentIndex] : null;

    public bool IsPlaying => _audio.IsPlaying;
    public string? PlaybackError { get { lock (_sync) return _error; } }
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
        var queue = tracks.ToList().AsReadOnly();
        lock (_sync)
        {
            _station = null;
            _video = null;
            _error = null;
            _consecutiveFailures = 0;
            _queue = queue;
            _order.Reset(_queue.Count, startIndex);
            if (queue.Count == 0) StopPlayback();
            else StartAudio();
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PlayRadio(RadioStation station)
    {
        lock (_sync)
        {
            _queue = [];
            _order.Reset(0, 0);
            _station = station;
            _video = null;
            _consecutiveFailures = 0;
            StartAudio();
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PlayVideo(VideoItem video)
    {
        lock (_sync)
        {
            _queue = [];
            _order.Reset(0, 0);
            _station = null;
            _video = video;
            _error = null;
            _audioPlaybackRequested = false;
            _canResumePausedInput = false;
            _audio.PlayVideo(video);
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StopVideo()
    {
        lock (_sync)
        {
            if (_video is null) return;
            StopPlayback();
            _video = null;
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    // These helpers run with _sync held. CurrentChanged is raised after releasing it.
    private void StartAudio()
    {
        try
        {
            _error = null;
            _canResumePausedInput = false;
            if (_station is { } station)
            {
                BeginAudioPlayback();
                _audio.PlayLocation(station.StreamUrl);
                return;
            }
            if (CurrentTrack is not { } track) return;
            BeginAudioPlayback();
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
            StopPlayback();
            _error = ex.Message;
        }
    }

    private void BeginAudioPlayback()
    {
#if ANDROID
        if (!_audioPlaybackRequested) MediaPlaybackForegroundService.Start();
#endif
        _audioPlaybackRequested = true;
    }

    private void StopPlayback()
    {
        _audioPlaybackRequested = false;
        _canResumePausedInput = false;
        _audio.Stop();
    }

    private void OnPlaybackCompleted(object? sender, (AudioPlayerService.Attempt Attempt, bool Failed) result)
    {
        lock (_sync)
        {
            if (!_audio.IsCurrent(result.Attempt)) return;
            if (_video is not null)
            {
                if (!result.Failed) return;
                StopPlayback();
                _error = "Das Video konnte nicht wiedergegeben werden.";
            }
            else
            {
                _canResumePausedInput = false;
                if (!_audioPlaybackRequested) return;
                if (result.Attempt.Started) _consecutiveFailures = 0;
                if (result.Failed) _consecutiveFailures++;

                if (_station is null && _consecutiveFailures < MaxConsecutiveFailures && _order.TryMoveNext(out _))
                    StartAudio();
                else
                {
                    StopPlayback();
                    if (result.Failed)
                        _error = _consecutiveFailures >= MaxConsecutiveFailures
                            ? "Wiedergabe nach drei aufeinanderfolgenden Fehlern gestoppt."
                            : "Das Medium konnte nicht wiedergegeben werden.";
                }
            }
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
        lock (_sync)
        {
            if (CurrentTrack is null)
            {
                _station = null;
                _video = null;
                _consecutiveFailures = 0;
                _queue = Array.AsReadOnly(new[] { track });
                _order.Reset(1, 0);
                StartAudio();
            }
            else
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
                _queue = queue.AsReadOnly();
            }
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
            _queue = queue.AsReadOnly();
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
                _queue = queue.AsReadOnly();
            }
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePlayPause()
    {
        lock (_sync)
        {
            if (_video is not null) _audio.TogglePlayPause();
            else SetAudioPaused(_audioPlaybackRequested);
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetAudioPaused(bool paused)
    {
        if (_audioPlaybackRequested != paused || (CurrentTrack is null && _station is null)) return;
        if (paused)
        {
            _audioPlaybackRequested = false;
            _canResumePausedInput = _audio.Pause();
            if (!_canResumePausedInput) _audio.Stop();
            return;
        }

        if (!_canResumePausedInput)
        {
            _consecutiveFailures = 0;
            StartAudio();
            return;
        }

        try
        {
            BeginAudioPlayback();
            _audio.Resume();
            _canResumePausedInput = false;
        }
        catch (Exception ex)
        {
            StopPlayback();
            _error = ex.Message;
        }
    }

    public void Next()
    {
        lock (_sync)
        {
            if (_station is not null || _video is not null) return;
            _consecutiveFailures = 0;
            if (_order.TryMoveNext(out _)) StartAudio();
            else StopPlayback();
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Previous()
    {
        lock (_sync)
        {
            if (_station is not null || _video is not null) return;
            if (_audio.Position.TotalSeconds > 3 || !_order.TryMovePrevious(out _))
                _audio.Seek(TimeSpan.Zero);
            else
            {
                _consecutiveFailures = 0;
                StartAudio();
            }
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(TimeSpan position)
    {
        lock (_sync) _audio.Seek(position);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

#if ANDROID
    internal (bool Requested, Track? Track, RadioStation? Station, bool HasNext) GetAudioSession()
    {
        lock (_sync) return (_audioPlaybackRequested, CurrentTrack, _station, _order.HasNext);
    }

    internal void SetAudioSessionPaused(bool paused)
    {
        lock (_sync)
        {
            if (_video is not null) return;
            SetAudioPaused(paused);
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void StopAudioSession()
    {
        lock (_sync)
        {
            if (!_audioPlaybackRequested) return;
            StopPlayback();
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }
#endif
}
