using Elyra.Models;
using LibVLCSharp.Shared;

namespace Elyra.Services;

/// <summary>
/// Native LibVLC playback engine. Two players share one LibVLC instance so the
/// next local track can start before the current one stops.
/// </summary>
public sealed class AudioPlayerService : IAudioPlayerService, IDisposable
{
    private readonly LibVLC _libVLC;
    private readonly MediaPlayer[] _players;
    private readonly string[] _sources = ["", ""];
    private readonly object _transitionSync = new();
    private CancellationTokenSource? _transitionCancellation;
    private int _activeIndex;
    private int _masterVolume = 100;
    private long _pendingStartPositionMs = -1;
    private volatile bool _transitionActive;
    private bool _disposed;

    public AudioPlayerService(PlaybackPreferencesService preferences)
    {
        // The normalizer is a LibVLC start-up filter. Changing the preference
        // therefore deliberately takes effect on the next application start.
        _libVLC = preferences.NormalizeVolume
            ? new LibVLC("--audio-filter=normvol", "--norm-max-level=2.0")
            : new LibVLC();

        _players = [new MediaPlayer(_libVLC), new MediaPlayer(_libVLC)];
        foreach (var player in _players)
        {
            player.Playing += OnPlaying;
            player.Paused += OnStateChanged;
            player.Stopped += OnStateChanged;
            player.EndReached += OnEndReached;
            player.EncounteredError += OnEncounteredError;
            player.TimeChanged += OnTimeChanged;
        }
    }

    public event EventHandler? StateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler? TrackEnded;
    public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

    private MediaPlayer ActivePlayer => _players[_activeIndex];
    public bool IsPlaying => ActivePlayer.IsPlaying;

    /// <summary>
    /// The video view is permanently bound to the primary player. PlayVideo
    /// switches playback back to that player before loading a video.
    /// </summary>
    public MediaPlayer MediaPlayer => _players[0];

    public TimeSpan Position =>
        TimeSpan.FromMilliseconds(ActivePlayer.Time < 0 ? 0 : ActivePlayer.Time);

    public TimeSpan Duration =>
        TimeSpan.FromMilliseconds(ActivePlayer.Length < 0 ? 0 : ActivePlayer.Length);

    public int Volume
    {
        get => _masterVolume;
        set
        {
            _masterVolume = Math.Clamp(value, 0, 100);
            if (!_transitionActive)
                ActivePlayer.Volume = _masterVolume;
        }
    }

    public void Play(string filePath, TimeSpan? startPosition = null)
    {
        CancelTransition();
        Interlocked.Exchange(
            ref _pendingStartPositionMs,
            startPosition is { } value && value > TimeSpan.Zero
                ? (long)value.TotalMilliseconds
                : -1);
        PlayOn(ActivePlayer, filePath, FromType.FromPath);
    }

    public void PlayLocation(string url)
    {
        CancelTransition();
        Interlocked.Exchange(ref _pendingStartPositionMs, -1);
        PlayOn(ActivePlayer, url, FromType.FromLocation);
    }

    public void PlayVideo(VideoItem video)
    {
        CancelTransition();
        SwitchToPrimaryPlayer();
        if (video.Kind == VideoSourceKind.Dvd)
            PlayOn(ActivePlayer, video.PlaybackLocation, FromType.FromLocation);
        else
            PlayOn(ActivePlayer, video.Source, FromType.FromPath);
    }

    public async Task<bool> CrossfadeToAsync(
        string filePath,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero || string.IsNullOrWhiteSpace(filePath))
            return false;

        CancellationTokenSource transitionCancellation;
        lock (_transitionSync)
        {
            _transitionCancellation?.Cancel();
            _transitionCancellation?.Dispose();
            _transitionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            transitionCancellation = _transitionCancellation;
            _transitionActive = true;
        }

        var sourceIndex = _activeIndex;
        var targetIndex = sourceIndex == 0 ? 1 : 0;
        var source = _players[sourceIndex];
        var target = _players[targetIndex];
        var token = transitionCancellation.Token;

        try
        {
            target.Stop();
            target.Volume = 0;
            PlayOn(target, filePath, FromType.FromPath);

            // LibVLC starts asynchronously. Do not fade out the current track
            // until the target player has actually begun playback.
            var readyUntil = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (!target.IsPlaying && DateTime.UtcNow < readyUntil)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(20, token).ConfigureAwait(false);
            }

            if (!target.IsPlaying)
                return false;

            var totalMilliseconds = Math.Max(1, duration.TotalMilliseconds);
            var steps = Math.Clamp((int)Math.Ceiling(totalMilliseconds / 40), 3, 300);
            var delay = TimeSpan.FromMilliseconds(totalMilliseconds / steps);
            for (var step = 1; step <= steps; step++)
            {
                token.ThrowIfCancellationRequested();
                var progress = (double)step / steps;
                var volume = _masterVolume;
                source.Volume = (int)Math.Round(volume * (1 - progress));
                target.Volume = (int)Math.Round(volume * progress);
                await Task.Delay(delay, token).ConfigureAwait(false);
            }

            _activeIndex = targetIndex;
            target.Volume = _masterVolume;
            source.Stop();
            StateChanged?.Invoke(this, EventArgs.Empty);
            PositionChanged?.Invoke(this, Position);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            if (_activeIndex == sourceIndex)
            {
                target.Stop();
                source.Volume = _masterVolume;
            }

            lock (_transitionSync)
            {
                if (ReferenceEquals(_transitionCancellation, transitionCancellation))
                {
                    _transitionCancellation.Dispose();
                    _transitionCancellation = null;
                    _transitionActive = false;
                }
            }
        }
    }

    public void CancelTransition()
    {
        CancellationTokenSource? cancellation;
        lock (_transitionSync)
        {
            cancellation = _transitionCancellation;
            _transitionCancellation = null;
            _transitionActive = false;
        }

        cancellation?.Cancel();
        cancellation?.Dispose();
        ActivePlayer.Volume = _masterVolume;
    }

    public void Pause()
    {
        CancelTransition();
        if (ActivePlayer.CanPause)
            ActivePlayer.SetPause(true);
    }

    public void Resume() => ActivePlayer.SetPause(false);

    public void TogglePlayPause()
    {
        CancelTransition();
        ActivePlayer.Pause();
    }

    public void Stop()
    {
        CancelTransition();
        ActivePlayer.Stop();
    }

    public void Seek(TimeSpan position)
    {
        CancelTransition();
        ActivePlayer.Time = (long)position.TotalMilliseconds;
    }

    private void PlayOn(MediaPlayer player, string source, FromType fromType)
    {
        _sources[Array.IndexOf(_players, player)] = source;
        player.Volume = _masterVolume;
        using var media = new Media(_libVLC, source, fromType);
        if (!player.Play(media) && IsActiveSender(player))
            _ = Task.Run(() => PlaybackFailed?.Invoke(
                this,
                new PlaybackFailedEventArgs(source)));
    }

    private void SwitchToPrimaryPlayer()
    {
        if (_activeIndex == 0)
            return;

        _players[1].Stop();
        _activeIndex = 0;
        _players[0].Volume = _masterVolume;
    }

    private bool IsActiveSender(object? sender) => ReferenceEquals(sender, ActivePlayer);

    private void OnPlaying(object? sender, EventArgs args)
    {
        if (!IsActiveSender(sender))
            return;

        var position = Interlocked.Exchange(ref _pendingStartPositionMs, -1);
        if (position >= 0)
        {
            var player = ActivePlayer;
            _ = Task.Run(() => player.Time = position);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnStateChanged(object? sender, EventArgs args)
    {
        if (IsActiveSender(sender))
            StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnEndReached(object? sender, EventArgs args)
    {
        if (IsActiveSender(sender) && !_transitionActive)
            TrackEnded?.Invoke(this, EventArgs.Empty);
    }

    private void OnEncounteredError(object? sender, EventArgs args)
    {
        if (!IsActiveSender(sender))
            return;

        var index = Array.IndexOf(_players, sender);
        var source = index >= 0 ? _sources[index] : "";
        PlaybackFailed?.Invoke(this, new PlaybackFailedEventArgs(source));
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs args)
    {
        if (IsActiveSender(sender))
            PositionChanged?.Invoke(this, TimeSpan.FromMilliseconds(args.Time));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        CancelTransition();
        foreach (var player in _players)
            player.Dispose();
        _libVLC.Dispose();
    }
}
