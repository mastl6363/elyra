using Elyra.Models;

namespace Elyra.Services;

/// <summary>
/// Owns the editable music queue, repeat/shuffle state and persistent listening session.
/// Radio and video use the same audio engine but do not overwrite the last music session.
/// </summary>
public sealed class PlaybackService
{
    private readonly IAudioPlayerService _audio;
    private readonly PlaybackSessionStore _sessionStore;
    private readonly UserMusicDataService _userMusic;
    private readonly PlaybackPreferencesService _preferences;
    private readonly PlaybackOrder _order = new();
    private List<Track> _queue = [];
    private TimeSpan _restoredPosition;
    private TimeSpan _lastPersistedPosition;
    private TimeSpan? _pendingPlaybackPosition;
    private bool _sessionPending;
    private int _transitionInProgress;
    private int _transitionGeneration;
    private string? _lastFailureSource;
    private DateTimeOffset _lastFailureAt;

    public PlaybackService(
        IAudioPlayerService audio,
        PlaybackSessionStore sessionStore,
        UserMusicDataService userMusic,
        PlaybackPreferencesService preferences)
    {
        _audio = audio;
        _sessionStore = sessionStore;
        _userMusic = userMusic;
        _preferences = preferences;

        RestoreSession();
        _audio.TrackEnded += (_, _) => Task.Run(HandleTrackEnded);
        _audio.PlaybackFailed += (_, failure) => Task.Run(() => HandlePlaybackFailure(failure));
        _audio.PositionChanged += OnPositionChanged;
        _audio.StateChanged += (_, _) =>
        {
            if (Current is not null) PersistSession();
        };
    }

    public IReadOnlyList<Track> Queue => _queue;
    public IReadOnlyList<PlaybackQueueItem> UpcomingQueue => _order.UpcomingIndices()
        .Where(index => index >= 0 && index < _queue.Count)
        .Select(index => new PlaybackQueueItem(index, _queue[index]))
        .ToList();
    public int CurrentIndex => _order.CurrentIndex;
    public Track? Current => CurrentIndex >= 0 && CurrentIndex < _queue.Count ? _queue[CurrentIndex] : null;
    public RadioStation? CurrentStation { get; private set; }
    public VideoItem? CurrentVideo { get; private set; }
    public bool HasCurrent => Current is not null || CurrentStation is not null || CurrentVideo is not null;
    public bool HasRestoredSession => _sessionPending && Current is not null;
    public bool ShuffleEnabled => _order.ShuffleEnabled;
    public PlaybackRepeatMode RepeatMode { get; private set; }
    public PlaybackIssue? Issue { get; private set; }

    public bool IsPlaying => _audio.IsPlaying;
    public TimeSpan Position => _sessionPending
        ? _restoredPosition
        : _pendingPlaybackPosition ?? _audio.Position;
    public TimeSpan Duration
    {
        get
        {
            if (_sessionPending && Current is not null)
                return Current.Duration;

            var engineDuration = _audio.Duration;
            return engineDuration > TimeSpan.Zero
                ? engineDuration
                : Current?.Duration ?? TimeSpan.Zero;
        }
    }

    public int Volume
    {
        get => _audio.Volume;
        set
        {
            _audio.Volume = Math.Clamp(value, 0, 100);
            PersistSession();
        }
    }

    public event EventHandler? CurrentChanged;

    public void Play(IEnumerable<Track> tracks, int startIndex = 0)
    {
        CancelTransition();
        ResetIssue();
        CurrentStation = null;
        CurrentVideo = null;
        _queue = tracks.ToList();
        _order.Reset(_queue.Count, startIndex);
        _sessionPending = false;
        _restoredPosition = TimeSpan.Zero;
        if (_queue.Count == 0)
        {
            _sessionStore.Clear();
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        StartCurrentTrack();
    }

    public void ResumeLastSession()
    {
        if (HasRestoredSession)
            StartCurrentTrack(_restoredPosition);
    }

    public void PlayRadio(RadioStation station)
    {
        CancelTransition();
        ResetIssue();
        CurrentStation = station;
        CurrentVideo = null;
        _queue = [];
        _order.Reset(0, 0);
        _sessionPending = false;
        _audio.PlayLocation(station.StreamUrl);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PlayVideo(VideoItem video)
    {
        CancelTransition();
        ResetIssue();
        CurrentStation = null;
        CurrentVideo = video;
        _queue = [];
        _order.Reset(0, 0);
        _sessionPending = false;
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

    public void PlayNext(Track track)
    {
        CancelTransition();
        if (Current is null)
        {
            Play([track]);
            return;
        }

        var insertIndex = CurrentIndex + 1;
        _queue.Insert(insertIndex, track);
        _order.Reset(_queue.Count, CurrentIndex);
        _order.PrioritizeNext(insertIndex);
        PersistAndNotify();
    }

    public void AddToQueue(Track track)
    {
        CancelTransition();
        if (Current is null)
        {
            Play([track]);
            return;
        }

        _queue.Add(track);
        _order.Reset(_queue.Count, CurrentIndex);
        PersistAndNotify();
    }

    public void RemoveQueueItem(int index)
    {
        CancelTransition();
        if (index <= CurrentIndex || index < 0 || index >= _queue.Count)
            return;
        _queue.RemoveAt(index);
        _order.Reset(_queue.Count, CurrentIndex);
        PersistAndNotify();
    }

    public void MoveQueueItem(int index, int offset)
    {
        CancelTransition();
        var target = index + offset;
        if (index <= CurrentIndex || target <= CurrentIndex || index >= _queue.Count || target >= _queue.Count)
            return;
        (_queue[index], _queue[target]) = (_queue[target], _queue[index]);
        _order.Reset(_queue.Count, CurrentIndex);
        PersistAndNotify();
    }

    public void ClearUpcoming()
    {
        CancelTransition();
        if (CurrentIndex < 0 || CurrentIndex + 1 >= _queue.Count)
            return;
        _queue.RemoveRange(CurrentIndex + 1, _queue.Count - CurrentIndex - 1);
        _order.Reset(_queue.Count, CurrentIndex);
        PersistAndNotify();
    }

    public void ToggleShuffle()
    {
        SetShuffle(!ShuffleEnabled);
    }

    public void SetShuffle(bool enabled)
    {
        if (ShuffleEnabled == enabled)
            return;
        CancelTransition();
        _order.SetShuffle(enabled);
        PersistAndNotify();
    }

    public void CycleRepeatMode()
    {
        SetRepeatMode(RepeatMode switch
        {
            PlaybackRepeatMode.Off => PlaybackRepeatMode.All,
            PlaybackRepeatMode.All => PlaybackRepeatMode.One,
            _ => PlaybackRepeatMode.Off
        });
    }

    public void SetRepeatMode(PlaybackRepeatMode mode)
    {
        if (RepeatMode == mode)
            return;
        CancelTransition();
        RepeatMode = mode;
        PersistAndNotify();
    }

    public void Retry()
    {
        ResetIssue();
        if (Current is not null)
        {
            StartCurrentTrack();
            return;
        }

        if (CurrentStation is not null)
            _audio.PlayLocation(CurrentStation.StreamUrl);
        else if (CurrentVideo is not null)
            _audio.PlayVideo(CurrentVideo);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DismissIssue()
    {
        if (Issue is null)
            return;
        Issue = null;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePlayPause()
    {
        CancelTransition();
        if (HasRestoredSession)
            ResumeLastSession();
        else if (HasCurrent)
        {
            if (_audio.IsPlaying)
                _audio.Pause();
            else
                _audio.Resume();
        }
    }

    public void Next()
    {
        if (CurrentStation is not null || CurrentVideo is not null) return;
        CancelTransition();
        MoveNext(allowRepeat: true);
    }

    public void Previous()
    {
        if (CurrentStation is not null || CurrentVideo is not null) return;
        CancelTransition();

        if (Position.TotalSeconds > 3)
        {
            Seek(TimeSpan.Zero);
        }
        else if (_order.TryMovePrevious(out _))
        {
            StartCurrentTrack();
        }
        else
        {
            Seek(TimeSpan.Zero);
        }
    }

    public void Seek(TimeSpan position)
    {
        CancelTransition();
        var safePosition = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        if (_sessionPending)
        {
            _restoredPosition = safePosition;
            PersistAndNotify();
            return;
        }
        _audio.Seek(safePosition);
        PersistSession();
    }

    private void HandleTrackEnded()
    {
        if (CurrentStation is not null || CurrentVideo is not null)
            return;

        if (RepeatMode == PlaybackRepeatMode.One)
            StartCurrentTrack();
        else
            MoveNext(allowRepeat: true);
    }

    private void MoveNext(bool allowRepeat)
    {
        _sessionPending = false;
        if (_order.TryMoveNext(out _))
        {
            StartCurrentTrack();
            return;
        }

        if (allowRepeat && RepeatMode == PlaybackRepeatMode.All && _queue.Count > 0)
        {
            _order.Reset(_queue.Count, 0);
            StartCurrentTrack();
            return;
        }

        _restoredPosition = TimeSpan.Zero;
        _sessionPending = Current is not null;
        _audio.Stop();
        PersistAndNotify();
    }

    private void StartCurrentTrack(TimeSpan? startPosition = null)
    {
        var track = Current;
        if (track is null)
            return;

        var missingTracks = new List<string>();
        while (!File.Exists(track.FilePath))
        {
            missingTracks.Add(track.Title);
            if (!_order.TryMoveNext(out _))
            {
                Issue = new PlaybackIssue(
                    missingTracks.Count == 1
                        ? $"„{missingTracks[0]}“ wurde nicht gefunden."
                        : $"{missingTracks.Count} nicht mehr vorhandene Titel wurden übersprungen.",
                    false);
                _audio.Stop();
                PersistAndNotify();
                return;
            }
            track = Current!;
            startPosition = null;
        }

        if (missingTracks.Count > 0)
        {
            Issue = new PlaybackIssue(
                missingTracks.Count == 1
                    ? $"„{missingTracks[0]}“ wurde nicht gefunden und übersprungen."
                    : $"{missingTracks.Count} nicht mehr vorhandene Titel wurden übersprungen.",
                false);
        }

        _sessionPending = false;
        _restoredPosition = TimeSpan.Zero;
        _pendingPlaybackPosition = startPosition is { } resume && resume > TimeSpan.Zero ? resume : null;
        _lastPersistedPosition = startPosition ?? TimeSpan.Zero;
        _audio.Play(track.FilePath, startPosition);
        _userMusic.RecordPlayed(track);
        PersistAndNotify();
    }

    private void RestoreSession()
    {
        var state = _sessionStore.Load();
        if (state is null)
            return;

        var requestedCurrentPath = state.CurrentIndex >= 0 && state.CurrentIndex < state.Queue.Count
            ? state.Queue[state.CurrentIndex].FilePath
            : null;
        _queue = state.Queue
            .Where(snapshot => File.Exists(snapshot.FilePath))
            .Select(snapshot => snapshot.ToTrack())
            .ToList();

        var restoredIndex = requestedCurrentPath is null
            ? -1
            : _queue.FindIndex(track =>
                string.Equals(track.FilePath, requestedCurrentPath, StringComparison.OrdinalIgnoreCase));
        if (restoredIndex < 0 && _queue.Count > 0)
            restoredIndex = Math.Clamp(state.CurrentIndex, 0, _queue.Count - 1);

        _order.Reset(_queue.Count, restoredIndex);
        _order.SetShuffle(state.ShuffleEnabled);
        RepeatMode = state.RepeatMode;
        _audio.Volume = Math.Clamp(state.Volume, 0, 100);
        _restoredPosition = TimeSpan.FromMilliseconds(Math.Max(0, state.PositionMs));
        _lastPersistedPosition = _restoredPosition;
        _sessionPending = restoredIndex >= 0;
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        if (_pendingPlaybackPosition is { } pending)
        {
            if (Math.Abs((position - pending).TotalSeconds) <= 2 || position > TimeSpan.FromSeconds(1))
            {
                _pendingPlaybackPosition = null;
                _lastPersistedPosition = position;
                PersistSession();
            }
            return;
        }

        TryStartAutomaticTransition(position);

        if (Current is null || Math.Abs((position - _lastPersistedPosition).TotalSeconds) < 5)
            return;
        _lastPersistedPosition = position;
        PersistSession();
    }

    private void TryStartAutomaticTransition(TimeSpan position)
    {
        var transitionSeconds = _preferences.TransitionSeconds;
        if (transitionSeconds <= 0 ||
            RepeatMode == PlaybackRepeatMode.One ||
            Current is null ||
            CurrentStation is not null ||
            CurrentVideo is not null ||
            Interlocked.CompareExchange(ref _transitionInProgress, 1, 0) != 0)
            return;

        var duration = _audio.Duration > TimeSpan.Zero ? _audio.Duration : Current.Duration;
        var remaining = duration - position;
        if (duration <= TimeSpan.Zero ||
            remaining <= TimeSpan.Zero ||
            remaining > TimeSpan.FromSeconds(transitionSeconds + 0.25))
        {
            Interlocked.Exchange(ref _transitionInProgress, 0);
            return;
        }

        var wrapsToStart = false;
        if (!_order.TryPeekNext(out var nextIndex))
        {
            if (RepeatMode != PlaybackRepeatMode.All || _queue.Count == 0)
            {
                Interlocked.Exchange(ref _transitionInProgress, 0);
                return;
            }
            nextIndex = 0;
            wrapsToStart = true;
        }

        var nextTrack = _queue[nextIndex];
        var generation = Volatile.Read(ref _transitionGeneration);
        _ = CompleteAutomaticTransitionAsync(
            nextTrack,
            wrapsToStart,
            TimeSpan.FromSeconds(Math.Min(transitionSeconds, Math.Max(0.05, remaining.TotalSeconds))),
            generation);
    }

    private async Task CompleteAutomaticTransitionAsync(
        Track nextTrack,
        bool wrapsToStart,
        TimeSpan duration,
        int generation)
    {
        try
        {
            if (!await _audio.CrossfadeToAsync(nextTrack.FilePath, duration))
            {
                if (generation == Volatile.Read(ref _transitionGeneration) &&
                    _order.TryMoveNext(out _))
                    StartCurrentTrack();
                return;
            }
            if (generation != Volatile.Read(ref _transitionGeneration))
                return;

            if (wrapsToStart)
                _order.Reset(_queue.Count, 0);
            else if (!_order.TryMoveNext(out _))
                return;

            _sessionPending = false;
            _restoredPosition = TimeSpan.Zero;
            _pendingPlaybackPosition = null;
            _lastPersistedPosition = TimeSpan.Zero;
            _userMusic.RecordPlayed(nextTrack);
            PersistAndNotify();
        }
        finally
        {
            Interlocked.Exchange(ref _transitionInProgress, 0);
        }
    }

    private void HandlePlaybackFailure(PlaybackFailedEventArgs failure)
    {
        if (!IsFailureForCurrentSource(failure.Source))
            return;

        if (string.Equals(_lastFailureSource, failure.Source, StringComparison.OrdinalIgnoreCase) &&
            DateTimeOffset.UtcNow - _lastFailureAt < TimeSpan.FromSeconds(1))
            return;

        _lastFailureSource = failure.Source;
        _lastFailureAt = DateTimeOffset.UtcNow;
        CancelTransition();

        if (Current is { } failedTrack)
        {
            if (_order.TryMoveNext(out _))
            {
                Issue = new PlaybackIssue(
                    $"„{failedTrack.Title}“ konnte nicht abgespielt werden und wurde übersprungen.",
                    false);
                StartCurrentTrack();
            }
            else
            {
                Issue = new PlaybackIssue(
                    $"„{failedTrack.Title}“ konnte nicht abgespielt werden.",
                    true);
                _audio.Stop();
                PersistAndNotify();
            }
            return;
        }

        if (CurrentStation is { } station)
        {
            Issue = new PlaybackIssue(
                $"„{station.Name}“ ist momentan nicht erreichbar.",
                true);
            _audio.Stop();
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (CurrentVideo is { } video)
        {
            Issue = new PlaybackIssue(
                $"„{video.Title}“ konnte nicht abgespielt werden.",
                true);
            _audio.Stop();
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool IsFailureForCurrentSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return true;
        if (Current is { } track)
            return string.Equals(source, track.FilePath, StringComparison.OrdinalIgnoreCase);
        if (CurrentStation is { } station)
            return string.Equals(source, station.StreamUrl, StringComparison.OrdinalIgnoreCase);
        if (CurrentVideo is { } video)
            return string.Equals(source, video.PlaybackLocation, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(source, video.Source, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private void ResetIssue()
    {
        Issue = null;
        _lastFailureSource = null;
        _lastFailureAt = default;
    }

    private void CancelTransition()
    {
        Interlocked.Increment(ref _transitionGeneration);
        _audio.CancelTransition();
        Interlocked.Exchange(ref _transitionInProgress, 0);
    }

    private void PersistAndNotify()
    {
        PersistSession();
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PersistSession()
    {
        if (_queue.Count == 0 || CurrentIndex < 0)
            return;

        _sessionStore.Save(new PlaybackSessionState
        {
            Queue = _queue.Select(TrackSnapshot.FromTrack).ToList(),
            CurrentIndex = CurrentIndex,
            PositionMs = (long)Position.TotalMilliseconds,
            ShuffleEnabled = ShuffleEnabled,
            RepeatMode = RepeatMode,
            Volume = Volume
        });
    }
}
