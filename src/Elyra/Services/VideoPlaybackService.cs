using Elyra.Models;

namespace Elyra.Services;

/// <summary>Opens a native video surface for the current platform.</summary>
public sealed class VideoPlaybackService : IDisposable
{
    private readonly AudioPlayerService _audio;
    private readonly PlaybackService _playback;
    private readonly VideoLibraryService _library;
#if ANDROID || IOS || MACCATALYST || WINDOWS
    private bool _isOpen;
#else
    private readonly object _gate = new();
    private LinuxVideoWindow? _window;
    private VideoItem? _video;
    private bool _resumeApplied;
#endif

    public VideoPlaybackService(
        AudioPlayerService audio,
        PlaybackService playback,
        VideoLibraryService library)
    {
        _audio = audio;
        _playback = playback;
        _library = library;
#if !ANDROID && !IOS && !MACCATALYST && !WINDOWS
        _playback.CurrentChanged += OnPlaybackChanged;
#endif
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

    public Task CloseAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        var root = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (root?.Navigation.ModalStack.LastOrDefault() is VideoPlayerPage)
            await root.Navigation.PopModalAsync(false);
    });
#else
    public Task OpenAsync(VideoItem video)
    {
        if (video.Kind != VideoSourceKind.File)
            throw new PlatformNotSupportedException("DVD-Wiedergabe ist im Linux-Desktop-Build noch nicht verfügbar.");
        if (!File.Exists(video.Source))
            throw new FileNotFoundException("Die Videodatei wurde nicht gefunden.", video.Source);

        lock (_gate)
        {
            if (_window is not null && _video?.Id == video.Id)
            {
                _window.Raise();
                return Task.CompletedTask;
            }

            CloseLinux();
            var window = new LinuxVideoWindow(video.Title, () => CloseLinux(), OnLinuxKey);
            _window = window;
            _video = video;
            _resumeApplied = false;
            try
            {
                _audio.MediaPlayer.EnableKeyInput = false;
                _audio.MediaPlayer.XWindow = window.WindowId;
                _audio.MediaPlayer.Playing += OnLinuxPlaying;
                _audio.MediaPlayer.EndReached += OnLinuxEnded;
                _playback.PlayVideo(video);
            }
            catch
            {
                CloseLinux();
                throw;
            }
        }

        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        CloseLinux();
        return Task.CompletedTask;
    }

    private void OnLinuxPlaying(object? sender, EventArgs args) => ThreadPool.QueueUserWorkItem(_ =>
    {
        lock (_gate)
        {
            if (_window is null || _video is null || _resumeApplied) return;
            _resumeApplied = true;
            if (_video.SavedPosition > TimeSpan.FromSeconds(5))
                _audio.Seek(_video.SavedPosition);
        }
    });

    private void OnLinuxEnded(object? sender, EventArgs args)
    {
        var videoId = _video?.Id;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            lock (_gate)
                if (_window is not null && _video?.Id == videoId)
                    CloseLinux(completed: true);
        });
    }

    private void OnPlaybackChanged(object? sender, EventArgs args)
    {
        lock (_gate)
            if (_window is not null && _playback.CurrentVideo?.Id != _video?.Id)
                CloseLinux(stopPlayback: false, savePosition: false);
    }

    private void OnLinuxKey(ulong key)
    {
        lock (_gate)
            if (_window is null) return;

        if (key == 0xff1b) CloseLinux(); // Escape
        else if (key == 0x20) _playback.TogglePlayPause(); // Space
        else if (key is 0xff51 or 0xff53) // Left/right: seek five seconds
        {
            var target = _audio.Position + TimeSpan.FromSeconds(key == 0xff51 ? -5 : 5);
            _audio.Seek(target < TimeSpan.Zero ? TimeSpan.Zero : target);
        }
    }

    private void CloseLinux(bool completed = false, bool stopPlayback = true, bool savePosition = true)
    {
        lock (_gate)
        {
            if (_window is null) return;
            var window = _window;
            var video = _video;
            _window = null;
            _video = null;

            _audio.MediaPlayer.Playing -= OnLinuxPlaying;
            _audio.MediaPlayer.EndReached -= OnLinuxEnded;
            if (video is not null && savePosition)
                _library.SavePosition(video.Id, completed ? TimeSpan.Zero : _audio.Position,
                    completed ? TimeSpan.Zero : _audio.Duration);
            if (stopPlayback && _playback.CurrentVideo?.Id == video?.Id)
                _playback.StopVideo();
            _audio.MediaPlayer.XWindow = 0;
            window.Dispose();
        }
    }
#endif

    public void Dispose()
    {
#if !ANDROID && !IOS && !MACCATALYST && !WINDOWS
        _playback.CurrentChanged -= OnPlaybackChanged;
        CloseLinux();
#endif
    }
}
