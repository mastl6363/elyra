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

    public AudioPlayerService()
    {
        // Core.Initialize() must already have run (see MauiProgram).
        _libVLC = new LibVLC();
        _player = new MediaPlayer(_libVLC);

        _player.Playing     += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        _player.Paused      += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        _player.Stopped     += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        _player.EndReached  += (_, _) => TrackEnded?.Invoke(this, EventArgs.Empty);
        _player.TimeChanged += (_, e) => PositionChanged?.Invoke(this, TimeSpan.FromMilliseconds(e.Time));
    }

    /// <summary>Fires when play/pause/stop state flips.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Fires roughly every ~250 ms while playing, with the new position.</summary>
    public event EventHandler<TimeSpan>? PositionChanged;

    /// <summary>Fires when the current track plays to its end (for auto-advance).</summary>
    public event EventHandler? TrackEnded;

    /// <summary>Fires when a requested local file no longer exists (e.g. a stale playlist entry).</summary>
    public event EventHandler? PlaybackFailed;

    public bool IsPlaying => _player.IsPlaying;
    public MediaPlayer MediaPlayer => _player;

    /// <summary>Current playback position.</summary>
    public TimeSpan Position => TimeSpan.FromMilliseconds(_player.Time < 0 ? 0 : _player.Time);

    /// <summary>Total length of the loaded track (0 if unknown/not loaded).</summary>
    public TimeSpan Duration => TimeSpan.FromMilliseconds(_player.Length < 0 ? 0 : _player.Length);

    /// <summary>Volume in the range 0–100. LibVLC reports -1 before media loads.</summary>
    public int Volume
    {
        get => _player.Volume < 0 ? 100 : _player.Volume;
        set => _player.Volume = Math.Clamp(value, 0, 100);
    }

    /// <summary>Loads and starts playing a local file path.</summary>
    public void Play(string filePath)
    {
        // A track can reference a file that was deleted or moved since it was added
        // to a playlist/library snapshot. Without this check, LibVLC fails to open
        // it asynchronously and playback silently gets stuck on the dead track.
        if (!File.Exists(filePath))
        {
            PlaybackFailed?.Invoke(this, EventArgs.Empty);
            return;
        }

        using var media = new Media(_libVLC, filePath, FromType.FromPath);
        _player.Play(media);
    }

    /// <summary>Loads and starts an HTTP(S) radio stream.</summary>
    public void PlayLocation(string url)
    {
        using var media = new Media(_libVLC, url, FromType.FromLocation);
        _player.Play(media);
    }

    public void PlayVideo(VideoItem video)
    {
        if (video.Kind == VideoSourceKind.Dvd)
            PlayLocation(video.PlaybackLocation);
        else
            Play(video.Source);
    }

    public void Pause()
    {
        if (_player.CanPause)
            _player.SetPause(true);
    }

    public void Resume() => _player.SetPause(false);

    /// <summary>Toggles between play and pause for the loaded track.</summary>
    public void TogglePlayPause() => _player.Pause();

    public void Stop() => _player.Stop();

    /// <summary>Seeks to an absolute position within the current track.</summary>
    public void Seek(TimeSpan position) => _player.Time = (long)position.TotalMilliseconds;

    public void Dispose()
    {
        _player.Dispose();
        _libVLC.Dispose();
    }
}
