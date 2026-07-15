using Elyra.Models;

namespace Elyra.Services;

/// <summary>Opens the native MAUI video surface above the Blazor application shell.</summary>
public sealed class VideoPlaybackService
{
    private readonly AudioPlayerService _audio;
    private readonly PlaybackService _playback;
    private readonly VideoLibraryService _library;
    private bool _isOpen;

    public VideoPlaybackService(
        AudioPlayerService audio,
        PlaybackService playback,
        VideoLibraryService library)
    {
        _audio = audio;
        _playback = playback;
        _library = library;
    }

    public Task OpenAsync(VideoItem video) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (_isOpen) return;

        var rootPage = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (rootPage is null)
            throw new InvalidOperationException("Das Elyra-Hauptfenster ist nicht verfügbar.");

        _isOpen = true;
        var page = new VideoPlayerPage(_audio, _playback, _library, video);
        page.Disappearing += (_, _) => _isOpen = false;
        await rootPage.Navigation.PushModalAsync(page, false);
    });
}
