using Elyra.Models;
using LibVLCSharp.Shared;

namespace Elyra.Services;

/// <summary>
/// Thin wrapper around LibVLCSharp that drives playback of local audio files
/// (MP3, FLAC, …). Framework-agnostic on purpose — the Razor UI subscribes to
/// the events and calls the methods, but this type knows nothing about Blazor.
/// Registered as a singleton; there is exactly one player per app session.
/// </summary>
public sealed class AudioPlayerService : IDisposable
{
    private readonly LibVLC _libVLC;
    private readonly MediaPlayer _player;
    private Attempt? _attempt;

    public AudioPlayerService()
    {
        // Core.Initialize() must already have run (see MauiProgram).
        _libVLC = OperatingSystem.IsWindows()
            ? Task.Run(() => new LibVLC()).GetAwaiter().GetResult()
            : new LibVLC();
        _player = new MediaPlayer(_libVLC);

        // LibVLC raises these on its own callback threads. Blazor may render
        // immediately inside an event handler and read MediaPlayer properties.
        // During a track transition that can wait on the same native VLC lock
        // held by Play(Media), deadlocking both the UI and playback. Never run
        // subscribers synchronously on a LibVLC callback thread.
        _player.Playing += (_, _) =>
        {
            var attempt = Volatile.Read(ref _attempt);
            if (attempt is not null) attempt.Started = true;
            Dispatch(() => StateChanged?.Invoke(this, EventArgs.Empty));
        };
        _player.Paused      += (_, _) => Dispatch(() => StateChanged?.Invoke(this, EventArgs.Empty));
        _player.Stopped += (_, _) =>
        {
            FinishCurrentAttempt(failed: true);
            Dispatch(() => StateChanged?.Invoke(this, EventArgs.Empty));
        };
        _player.EndReached += (_, _) => FinishCurrentAttempt(failed: false);
        _player.EncounteredError += (_, _) => FinishCurrentAttempt(failed: true);
        _player.TimeChanged += (_, e) =>
        {
            var position = TimeSpan.FromMilliseconds(e.Time);
            Dispatch(() => PositionChanged?.Invoke(this, position));
        };
    }

    /// <summary>Fires when play/pause/stop state flips.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Fires roughly every ~250 ms while playing, with the new position.</summary>
    public event EventHandler<TimeSpan>? PositionChanged;

    internal sealed class Attempt
    {
        internal volatile bool Started;
        internal int Completed;
    }

    internal event EventHandler<(Attempt Attempt, bool Failed)>? PlaybackCompleted;

    internal bool IsCurrent(Attempt attempt) => ReferenceEquals(Volatile.Read(ref _attempt), attempt);

    private void FinishCurrentAttempt(bool failed)
    {
        if (Volatile.Read(ref _attempt) is { } attempt) Finish(attempt, failed);
    }

    private void Finish(Attempt attempt, bool failed)
    {
        if (Interlocked.Exchange(ref attempt.Completed, 1) != 0) return;
        // LibVLC can report an unreadable input as EndReached without ever reaching Playing.
        var result = (attempt, failed || !attempt.Started);
        Dispatch(() => PlaybackCompleted?.Invoke(this, result));
    }

    private static void Dispatch(Action action) => ThreadPool.QueueUserWorkItem(
        static state => ((Action)state!).Invoke(), action);

    public bool IsPlaying => _player.IsPlaying;
    public MediaPlayer MediaPlayer => _player;

    /// <summary>Current playback position.</summary>
    public TimeSpan Position => TimeSpan.FromMilliseconds(Math.Max(0, _player.Time));

    /// <summary>Total length of the loaded track (0 if unknown/not loaded).</summary>
    public TimeSpan Duration => TimeSpan.FromMilliseconds(Math.Max(0, _player.Length));

    /// <summary>Volume in the range 0–100. LibVLC reports -1 before media loads.</summary>
    public int Volume
    {
        get => _player.Volume < 0 ? 100 : _player.Volume;
        set => _player.Volume = Math.Clamp(value, 0, 100);
    }

    /// <summary>Loads and starts playing a local file path.</summary>
    public void Play(string filePath) => PlayMedia(filePath, FromType.FromPath, audioOnly: true);

    /// <summary>Loads and starts an HTTP(S) audio stream.</summary>
    public void PlayLocation(string url) => PlayMedia(url, FromType.FromLocation, audioOnly: true);

    public void PlayVideo(VideoItem video) => PlayMedia(video.PlaybackLocation,
        video.Kind == VideoSourceKind.Dvd ? FromType.FromLocation : FromType.FromPath, audioOnly: false);

    private void PlayMedia(string source, FromType type, bool audioOnly)
    {
        // Stop drains the previous input before a new attempt can receive native events.
        if (Volatile.Read(ref _attempt) is not null) Stop();
        var attempt = new Attempt();
        Volatile.Write(ref _attempt, attempt);
        if (type == FromType.FromPath && !File.Exists(source))
        {
            Finish(attempt, failed: true);
            return;
        }

        using var media = new Media(_libVLC, source, type);
        if (audioOnly) media.AddOption(":no-video");
        if (!_player.Play(media)) Finish(attempt, failed: true);
    }

    public bool Pause()
    {
        if (!_player.CanPause) return false;
        _player.SetPause(true);
        return true;
    }

    public void Resume() => _player.SetPause(false);

    /// <summary>Toggles between play and pause for the loaded track.</summary>
    public void TogglePlayPause() => _player.Pause();

    public void Stop()
    {
        Volatile.Write(ref _attempt, null);
        _player.Stop();
    }

    /// <summary>Seeks to an absolute position within the current track.</summary>
    public void Seek(TimeSpan position) => _player.Time = (long)position.TotalMilliseconds;

    public void Dispose()
    {
        _player.Dispose();
        _libVLC.Dispose();
    }
}
