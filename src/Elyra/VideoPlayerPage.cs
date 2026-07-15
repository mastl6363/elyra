using Elyra.Models;
using Elyra.Services;
using LibVLCSharp.MAUI;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;

namespace Elyra;

/// <summary>Native, full-window video surface used for movies and DVD navigation.</summary>
public sealed class VideoPlayerPage : ContentPage
{
    private readonly AudioPlayerService _audio;
    private readonly PlaybackService _playback;
    private readonly VideoLibraryService _library;
    private readonly VideoItem _video;
    private readonly Slider _progress;
    private readonly Label _elapsed;
    private readonly Label _duration;
    private readonly Button _playPause;
    private readonly Picker _audioTracks;
    private readonly Picker _subtitles;
    private readonly List<int> _audioTrackIds = [];
    private readonly List<int> _subtitleIds = [];
    private readonly IDispatcherTimer _timer;
    private bool _isSeeking;
    private bool _resumeApplied;
    private bool _closed;

    public VideoPlayerPage(
        AudioPlayerService audio,
        PlaybackService playback,
        VideoLibraryService library,
        VideoItem video)
    {
        _audio = audio;
        _playback = playback;
        _library = library;
        _video = video;

        BackgroundColor = Color.FromArgb("#050507");
        Shell.SetNavBarIsVisible(this, false);

        var videoView = new VideoView
        {
            MediaPlayer = audio.MediaPlayer,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        var close = Button("✕", CloseAsync, 18);
        close.WidthRequest = 44;
        close.HeightRequest = 44;

        var title = new Label
        {
            Text = video.Title,
            TextColor = Colors.White,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        var topBar = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(new GridLength(52)), new ColumnDefinition(GridLength.Star)],
            Padding = new Thickness(16, 12),
            BackgroundColor = Color.FromArgb("#B0000000"),
            Children = { close, title }
        };
        Grid.SetColumn(title, 1);

        _playPause = Button("⏸", TogglePlayPause, 24);
        var rewind = Button("−10", () => SeekRelative(-10), 14);
        var forward = Button("+10", () => SeekRelative(10), 14);
        var previousChapter = Button("◀|", audio.MediaPlayer.PreviousChapter, 14);
        var nextChapter = Button("|▶", audio.MediaPlayer.NextChapter, 14);

        _elapsed = TimeLabel();
        _duration = TimeLabel();
        _progress = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            MinimumTrackColor = Color.FromArgb("#9D7BFF"),
            MaximumTrackColor = Color.FromArgb("#3A3A45"),
            ThumbColor = Colors.White,
            HorizontalOptions = LayoutOptions.Fill
        };
        _progress.DragStarted += (_, _) => _isSeeking = true;
        _progress.DragCompleted += (_, _) =>
        {
            _audio.Seek(TimeSpan.FromSeconds(_progress.Value));
            _isSeeking = false;
        };

        var timeline = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(new GridLength(62)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(62))],
            ColumnSpacing = 10
        };
        timeline.Add(_elapsed, 0, 0);
        timeline.Add(_progress, 1, 0);
        timeline.Add(_duration, 2, 0);

        _audioTracks = TrackPicker("Tonspur");
        _audioTracks.SelectedIndexChanged += (_, _) => SelectTrack(_audioTracks, _audioTrackIds, id => audio.MediaPlayer.SetAudioTrack(id));
        _subtitles = TrackPicker("Untertitel");
        _subtitles.SelectedIndexChanged += (_, _) => SelectTrack(_subtitles, _subtitleIds, id => audio.MediaPlayer.SetSpu(id));

        var buttons = new HorizontalStackLayout
        {
            Spacing = 12,
            HorizontalOptions = LayoutOptions.Center,
            Children = { previousChapter, rewind, _playPause, forward, nextChapter, _audioTracks, _subtitles }
        };

        var controls = new VerticalStackLayout
        {
            Spacing = 10,
            Padding = new Thickness(24, 12, 24, 18),
            BackgroundColor = Color.FromArgb("#D0101015"),
            Children = { timeline, buttons }
        };

        var root = new Grid
        {
            RowDefinitions = [new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)],
            Children = { videoView, controls }
        };
        Grid.SetRow(controls, 1);
        root.Add(topBar, 0, 0);

        if (video.Kind == VideoSourceKind.Dvd)
        {
            var dvdNavigation = BuildDvdNavigation();
            root.Add(dvdNavigation, 0, 0);
            dvdNavigation.VerticalOptions = LayoutOptions.End;
            dvdNavigation.Margin = new Thickness(0, 0, 0, 16);
        }

        Content = root;

        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += (_, _) => RefreshProgress();

        audio.MediaPlayer.Playing += OnPlaying;
        audio.MediaPlayer.Paused += OnStateChanged;
        audio.MediaPlayer.Stopped += OnStateChanged;
        audio.MediaPlayer.EndReached += OnEnded;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        DeviceDisplay.Current.KeepScreenOn = true;
        _playback.PlayVideo(_video);
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        _timer.Stop();
        DeviceDisplay.Current.KeepScreenOn = false;
        SavePosition();
        _playback.StopVideo();
        DetachEvents();
        base.OnDisappearing();
    }

    private void OnPlaying(object? sender, EventArgs args) => Dispatcher.Dispatch(() =>
    {
        _playPause.Text = "⏸";
        PopulateTracks();
        if (!_resumeApplied && _video.Kind == VideoSourceKind.File && _video.SavedPosition > TimeSpan.FromSeconds(5))
        {
            _audio.Seek(_video.SavedPosition);
            _resumeApplied = true;
        }
    });

    private void OnStateChanged(object? sender, EventArgs args) =>
        Dispatcher.Dispatch(() => _playPause.Text = _audio.IsPlaying ? "⏸" : "▶");

    private void OnEnded(object? sender, EventArgs args) => Dispatcher.Dispatch(() =>
    {
        _playPause.Text = "▶";
        if (_video.Kind == VideoSourceKind.File)
            _library.SavePosition(_video.Id, TimeSpan.Zero, TimeSpan.Zero);
    });

    private void RefreshProgress()
    {
        var position = _audio.Position;
        var duration = _audio.Duration;
        _elapsed.Text = Format(position);
        _duration.Text = Format(duration);
        _progress.Maximum = Math.Max(1, duration.TotalSeconds);
        if (!_isSeeking) _progress.Value = Math.Clamp(position.TotalSeconds, 0, _progress.Maximum);
    }

    private void TogglePlayPause()
    {
        _playback.TogglePlayPause();
        _playPause.Text = _audio.IsPlaying ? "⏸" : "▶";
    }

    private void SeekRelative(int seconds)
    {
        var target = _audio.Position + TimeSpan.FromSeconds(seconds);
        _audio.Seek(target < TimeSpan.Zero ? TimeSpan.Zero : target);
    }

    private async void CloseAsync()
    {
        if (_closed) return;
        _closed = true;
        SavePosition();
        await Navigation.PopModalAsync(false);
    }

    private void SavePosition()
    {
        if (_video.Kind == VideoSourceKind.File)
            _library.SavePosition(_video.Id, _audio.Position, _audio.Duration);
    }

    private void PopulateTracks()
    {
        PopulatePicker(_audioTracks, _audioTrackIds, _audio.MediaPlayer.AudioTrackDescription, _audio.MediaPlayer.AudioTrack);
        PopulatePicker(_subtitles, _subtitleIds, _audio.MediaPlayer.SpuDescription, _audio.MediaPlayer.Spu);
    }

    private static void PopulatePicker(Picker picker, List<int> ids, TrackDescription[] tracks, int selectedId)
    {
        ids.Clear();
        picker.Items.Clear();
        foreach (var track in tracks)
        {
            ids.Add(track.Id);
            picker.Items.Add(track.Name);
        }
        picker.SelectedIndex = ids.IndexOf(selectedId);
        picker.IsVisible = ids.Count > 1;
    }

    private static void SelectTrack(Picker picker, IReadOnlyList<int> ids, Action<int> setter)
    {
        if (picker.SelectedIndex >= 0 && picker.SelectedIndex < ids.Count)
            setter(ids[picker.SelectedIndex]);
    }

    private View BuildDvdNavigation()
    {
        var menu = Button("DVD-Menü", () => _audio.MediaPlayer.Navigate(5), 13);
        var left = Button("←", () => _audio.MediaPlayer.Navigate(3), 18);
        var up = Button("↑", () => _audio.MediaPlayer.Navigate(1), 18);
        var down = Button("↓", () => _audio.MediaPlayer.Navigate(2), 18);
        var right = Button("→", () => _audio.MediaPlayer.Navigate(4), 18);
        var activate = Button("OK", () => _audio.MediaPlayer.Navigate(0), 13);
        return new HorizontalStackLayout
        {
            Spacing = 8,
            Padding = new Thickness(16, 8),
            HorizontalOptions = LayoutOptions.Center,
            BackgroundColor = Color.FromArgb("#B0000000"),
            Children = { menu, left, up, activate, down, right }
        };
    }

    private void DetachEvents()
    {
        _audio.MediaPlayer.Playing -= OnPlaying;
        _audio.MediaPlayer.Paused -= OnStateChanged;
        _audio.MediaPlayer.Stopped -= OnStateChanged;
        _audio.MediaPlayer.EndReached -= OnEnded;
    }

    private static Button Button(string text, Action action, double fontSize) => new()
    {
        Text = text,
        FontSize = fontSize,
        TextColor = Colors.White,
        BackgroundColor = Color.FromArgb("#282832"),
        CornerRadius = 22,
        Padding = new Thickness(12, 6),
        Command = new Command(action)
    };

    private static Picker TrackPicker(string title) => new()
    {
        Title = title,
        TextColor = Colors.White,
        TitleColor = Color.FromArgb("#A1A1AA"),
        BackgroundColor = Color.FromArgb("#282832"),
        WidthRequest = 150,
        IsVisible = false
    };

    private static Label TimeLabel() => new()
    {
        Text = "0:00",
        TextColor = Color.FromArgb("#A1A1AA"),
        FontSize = 12,
        HorizontalTextAlignment = TextAlignment.Center,
        VerticalTextAlignment = TextAlignment.Center
    };

    private static string Format(TimeSpan value) =>
        value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
}
