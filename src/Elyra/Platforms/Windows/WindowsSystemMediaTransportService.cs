using Elyra.Models;
using Microsoft.Maui.ApplicationModel;
using Windows.Media;
using Windows.Storage.Streams;

namespace Elyra.Services;

/// <summary>
/// Connects Elyra's LibVLC-based playback to the Windows media overlay, hardware
/// media keys and the system timeline.
/// </summary>
public sealed class WindowsSystemMediaTransportService : ISystemMediaTransportService, IDisposable
{
    private readonly PlaybackService _playback;
    private readonly IAudioPlayerService _audio;
    private SystemMediaTransportControls? _controls;
    private IRandomAccessStream? _thumbnailStream;
    private DateTimeOffset _lastTimelineUpdate;
    private int _metadataGeneration;
    private bool _disposed;

    public WindowsSystemMediaTransportService(
        PlaybackService playback,
        IAudioPlayerService audio)
    {
        _playback = playback;
        _audio = audio;
    }

    public void Initialize(Window window)
    {
        if (_controls is not null ||
            window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
            return;

        var handle = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        _controls = SystemMediaTransportControlsInterop.GetForWindow(handle);
        _controls.IsEnabled = true;
        _controls.IsPlayEnabled = true;
        _controls.IsPauseEnabled = true;
        _controls.ButtonPressed += OnButtonPressed;
        _controls.PlaybackPositionChangeRequested += OnPlaybackPositionChangeRequested;
        _controls.ShuffleEnabledChangeRequested += OnShuffleEnabledChangeRequested;
        _controls.AutoRepeatModeChangeRequested += OnAutoRepeatModeChangeRequested;

        _playback.CurrentChanged += OnPlaybackChanged;
        _audio.StateChanged += OnPlaybackChanged;
        _audio.PositionChanged += OnPositionChanged;

        UpdateAll();
    }

    private void OnButtonPressed(
        SystemMediaTransportControls sender,
        SystemMediaTransportControlsButtonPressedEventArgs args) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play when !_playback.IsPlaying:
                    _playback.TogglePlayPause();
                    break;
                case SystemMediaTransportControlsButton.Pause when _playback.IsPlaying:
                    _playback.TogglePlayPause();
                    break;
                case SystemMediaTransportControlsButton.Next:
                    _playback.Next();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    _playback.Previous();
                    break;
            }
        });

    private void OnPlaybackPositionChangeRequested(
        SystemMediaTransportControls sender,
        PlaybackPositionChangeRequestedEventArgs args)
    {
        if (_playback.CurrentStation is null)
            MainThread.BeginInvokeOnMainThread(() => _playback.Seek(args.RequestedPlaybackPosition));
    }

    private void OnShuffleEnabledChangeRequested(
        SystemMediaTransportControls sender,
        ShuffleEnabledChangeRequestedEventArgs args) =>
        MainThread.BeginInvokeOnMainThread(() => _playback.SetShuffle(args.RequestedShuffleEnabled));

    private void OnAutoRepeatModeChangeRequested(
        SystemMediaTransportControls sender,
        AutoRepeatModeChangeRequestedEventArgs args) =>
        MainThread.BeginInvokeOnMainThread(() => _playback.SetRepeatMode(args.RequestedAutoRepeatMode switch
        {
            MediaPlaybackAutoRepeatMode.Track => PlaybackRepeatMode.One,
            MediaPlaybackAutoRepeatMode.List => PlaybackRepeatMode.All,
            _ => PlaybackRepeatMode.Off
        }));

    private void OnPlaybackChanged(object? sender, EventArgs args) =>
        MainThread.BeginInvokeOnMainThread(UpdateAll);

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        if (DateTimeOffset.UtcNow - _lastTimelineUpdate < TimeSpan.FromSeconds(5))
            return;

        _lastTimelineUpdate = DateTimeOffset.UtcNow;
        MainThread.BeginInvokeOnMainThread(UpdateTimeline);
    }

    private void UpdateAll()
    {
        if (_controls is null)
            return;

        _controls.IsEnabled = _playback.HasCurrent;
        _controls.IsPlayEnabled = _playback.HasCurrent;
        _controls.IsPauseEnabled = _playback.HasCurrent;
        _controls.IsNextEnabled = _playback.Current is not null && _playback.Queue.Count > 1;
        _controls.IsPreviousEnabled = _playback.Current is not null;
        _controls.ShuffleEnabled = _playback.ShuffleEnabled;
        _controls.AutoRepeatMode = _playback.RepeatMode switch
        {
            PlaybackRepeatMode.One => MediaPlaybackAutoRepeatMode.Track,
            PlaybackRepeatMode.All => MediaPlaybackAutoRepeatMode.List,
            _ => MediaPlaybackAutoRepeatMode.None
        };
        _controls.PlaybackStatus = !_playback.HasCurrent
            ? MediaPlaybackStatus.Closed
            : _playback.IsPlaying
                ? MediaPlaybackStatus.Playing
                : MediaPlaybackStatus.Paused;

        UpdateTimeline();
        _ = UpdateMetadataAsync(Interlocked.Increment(ref _metadataGeneration));
    }

    private async Task UpdateMetadataAsync(int generation)
    {
        var controls = _controls;
        if (controls is null)
            return;

        var track = _playback.Current;
        var station = _playback.CurrentStation;
        var video = _playback.CurrentVideo;
        var updater = controls.DisplayUpdater;
        updater.ClearAll();

        if (track is not null)
        {
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = track.Title;
            updater.MusicProperties.Artist = track.Artist;
            updater.MusicProperties.AlbumTitle = track.Album;
            updater.MusicProperties.AlbumArtist = track.AlbumArtist;
            await SetThumbnailAsync(updater, track.CoverArtDataUri);
        }
        else if (station is not null)
        {
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = station.Name;
            updater.MusicProperties.Artist = string.IsNullOrWhiteSpace(station.Country)
                ? "Internetradio"
                : station.Country;
            await SetThumbnailAsync(updater, station.Favicon);
        }
        else if (video is not null)
        {
            updater.Type = MediaPlaybackType.Video;
            updater.VideoProperties.Title = video.Title;
            updater.VideoProperties.Subtitle = video.Kind == VideoSourceKind.Dvd ? "DVD" : "Film";
        }
        else
        {
            updater.Update();
            return;
        }

        if (generation == Volatile.Read(ref _metadataGeneration))
            updater.Update();
    }

    private async Task SetThumbnailAsync(
        SystemMediaTransportControlsDisplayUpdater updater,
        string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return;

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            updater.Thumbnail = RandomAccessStreamReference.CreateFromUri(uri);
            return;
        }

        var separator = source.IndexOf(',');
        if (!source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) || separator < 0)
            return;

        try
        {
            var bytes = Convert.FromBase64String(source[(separator + 1)..]);
            var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var previous = _thumbnailStream;
            _thumbnailStream = stream;
            updater.Thumbnail = RandomAccessStreamReference.CreateFromStream(stream);
            previous?.Dispose();
        }
        catch (FormatException)
        {
            // Invalid embedded artwork must never interrupt playback.
        }
    }

    private void UpdateTimeline()
    {
        if (_controls is null || _playback.CurrentStation is not null)
            return;

        var duration = _playback.Duration < TimeSpan.Zero ? TimeSpan.Zero : _playback.Duration;
        var position = _playback.Position < TimeSpan.Zero
            ? TimeSpan.Zero
            : _playback.Position > duration && duration > TimeSpan.Zero
                ? duration
                : _playback.Position;

        _controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero,
            MinSeekTime = TimeSpan.Zero,
            Position = position,
            MaxSeekTime = duration,
            EndTime = duration
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _playback.CurrentChanged -= OnPlaybackChanged;
        _audio.StateChanged -= OnPlaybackChanged;
        _audio.PositionChanged -= OnPositionChanged;
        if (_controls is not null)
        {
            _controls.ButtonPressed -= OnButtonPressed;
            _controls.PlaybackPositionChangeRequested -= OnPlaybackPositionChangeRequested;
            _controls.ShuffleEnabledChangeRequested -= OnShuffleEnabledChangeRequested;
            _controls.AutoRepeatModeChangeRequested -= OnAutoRepeatModeChangeRequested;
            _controls.IsEnabled = false;
        }
        _thumbnailStream?.Dispose();
    }
}
