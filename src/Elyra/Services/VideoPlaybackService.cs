using Elyra.Models;

namespace Elyra.Services;

/// <summary>Opens the native MAUI video surface above the Blazor application shell.</summary>
public sealed class VideoPlaybackService
{
    private readonly AudioPlayerService _audio;
    private readonly PlaybackService _playback;
    private readonly VideoLibraryService _library;
#if ANDROID || IOS || MACCATALYST || WINDOWS
    private bool _isOpen;
#endif

    public VideoPlaybackService(
        AudioPlayerService audio,
        PlaybackService playback,
        VideoLibraryService library)
    {
        _audio = audio;
        _playback = playback;
        _library = library;
    }

#if ANDROID || IOS || MACCATALYST || WINDOWS
    public Task OpenAsync(VideoItem video) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (_isOpen) return;

        var rootPage = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (rootPage is null)
            throw new InvalidOperationException("Das Elyra-Hauptfenster ist nicht verfügbar.");

        _isOpen = true;
        try
        {
            var page = new VideoPlayerPage(_audio, _playback, _library, video);
            page.Disappearing += (_, _) => _isOpen = false;
            await rootPage.Navigation.PushModalAsync(page, false);
        }
        catch
        {
            // Page construction or the modal push failed before Disappearing could
            // ever fire to reset the flag — without this, video playback would be
            // permanently locked out for the rest of the app session.
            _isOpen = false;
            throw;
        }
    });
#else
    // The native video surface (VideoPlayerPage) is a MAUI ContentPage; the Linux
    // desktop host has no MAUI window to push it onto. Movie/DVD playback stays
    // Windows/Android-only until a Linux-native video surface exists.
    public Task OpenAsync(VideoItem video) => throw new PlatformNotSupportedException(
        "Video-/DVD-Wiedergabe ist im Linux-Desktop-Build noch nicht verfügbar.");
#endif
}
