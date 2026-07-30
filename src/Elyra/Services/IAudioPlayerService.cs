using Elyra.Models;

namespace Elyra.Services;

/// <summary>Playback engine boundary, allowing queue and session behavior to be tested without libVLC.</summary>
public interface IAudioPlayerService
{
    event EventHandler? StateChanged;
    event EventHandler<TimeSpan>? PositionChanged;
    event EventHandler? TrackEnded;
    event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

    bool IsPlaying { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    int Volume { get; set; }

    void Play(string filePath, TimeSpan? startPosition = null);
    void PlayLocation(string url);
    void PlayVideo(VideoItem video);
    Task<bool> CrossfadeToAsync(string filePath, TimeSpan duration, CancellationToken cancellationToken = default);
    void CancelTransition();
    void TogglePlayPause();
    void Stop();
    void Seek(TimeSpan position);
}
