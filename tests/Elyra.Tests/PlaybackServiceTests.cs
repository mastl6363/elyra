using Elyra.Models;
using Elyra.Services;

namespace Elyra.Tests;

public sealed class PlaybackServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elyra-playback-{Guid.NewGuid():N}");
    private string SessionPath => Path.Combine(_directory, "session.json");
    private string UserDataPath => Path.Combine(_directory, "user-music.json");
    private string PreferencesPath => Path.Combine(_directory, "preferences.json");

    [Fact]
    public void Queue_CanInsertMoveRemoveAndClearUpcomingTracks()
    {
        var audio = new FakeAudioPlayer();
        var service = CreateService(audio);
        var first = Track("first.mp3");
        var second = Track("second.mp3");
        var next = Track("next.mp3");
        var later = Track("later.mp3");
        service.Play([first, second]);

        service.PlayNext(next);
        service.AddToQueue(later);

        Assert.Equal(["first.mp3", "next.mp3", "second.mp3", "later.mp3"],
            service.Queue.Select(track => Path.GetFileName(track.FilePath)));

        service.MoveQueueItem(3, -1);
        service.RemoveQueueItem(1);
        Assert.Equal(["first.mp3", "later.mp3", "second.mp3"],
            service.Queue.Select(track => Path.GetFileName(track.FilePath)));

        service.ClearUpcoming();
        Assert.Single(service.Queue);
        Assert.Equal(first.FilePath, service.Current?.FilePath);
    }

    [Fact]
    public void RepeatOne_ReplaysCurrentTrackWhenItEnds()
    {
        var audio = new FakeAudioPlayer();
        var service = CreateService(audio);
        var track = Track("repeat.mp3");
        service.Play([track]);
        service.CycleRepeatMode();
        service.CycleRepeatMode();

        audio.RaiseEnded();
        SpinWait.SpinUntil(() => audio.Played.Count == 2, TimeSpan.FromSeconds(2));

        Assert.Equal(PlaybackRepeatMode.One, service.RepeatMode);
        Assert.Equal([track.FilePath, track.FilePath], audio.Played.Select(item => item.Path));
    }

    [Fact]
    public void RepeatAll_ReturnsToStartAfterLastTrack()
    {
        var audio = new FakeAudioPlayer();
        var service = CreateService(audio);
        var first = Track("first.mp3");
        var second = Track("second.mp3");
        service.Play([first, second], 1);
        service.CycleRepeatMode();

        audio.RaiseEnded();
        SpinWait.SpinUntil(() => audio.Played.Count == 2, TimeSpan.FromSeconds(2));

        Assert.Equal(first.FilePath, service.Current?.FilePath);
        Assert.Equal(first.FilePath, audio.Played.Last().Path);
    }

    [Fact]
    public void Session_RestoresQueuePositionModesAndVolumeWithoutAutoplay()
    {
        var firstAudio = new FakeAudioPlayer();
        var firstService = CreateService(firstAudio);
        var first = Track("first.mp3");
        var second = Track("second.mp3");
        firstService.Play([first, second], 1);
        firstService.Seek(TimeSpan.FromSeconds(42));
        firstService.Volume = 61;
        firstService.ToggleShuffle();
        firstService.CycleRepeatMode();

        var restoredAudio = new FakeAudioPlayer();
        var restored = CreateService(restoredAudio);

        Assert.True(restored.HasRestoredSession);
        Assert.False(restoredAudio.IsPlaying);
        Assert.Equal(second.FilePath, restored.Current?.FilePath);
        Assert.Equal(TimeSpan.FromSeconds(42), restored.Position);
        Assert.Equal(61, restored.Volume);
        Assert.True(restored.ShuffleEnabled);
        Assert.Equal(PlaybackRepeatMode.All, restored.RepeatMode);

        restored.ResumeLastSession();
        var playback = Assert.Single(restoredAudio.Played);
        Assert.Equal(second.FilePath, playback.Path);
        Assert.Equal(TimeSpan.FromSeconds(42), playback.StartPosition);
    }

    [Fact]
    public void RadioPlayback_DoesNotOverwriteLastMusicSession()
    {
        var service = CreateService(new FakeAudioPlayer());
        var track = Track("music.mp3");
        service.Play([track]);
        service.Seek(TimeSpan.FromSeconds(25));

        service.PlayRadio(new RadioStation
        {
            StationUuid = "radio",
            Name = "Radio",
            ResolvedUrl = "https://example.test/stream"
        });
        service.Volume = 45;

        var restored = CreateService(new FakeAudioPlayer());
        Assert.True(restored.HasRestoredSession);
        Assert.Equal(track.FilePath, restored.Current?.FilePath);
        Assert.Equal(TimeSpan.FromSeconds(25), restored.Position);
    }

    [Fact]
    public void NearTrackEnd_CrossfadesAndAdvancesToNextTrack()
    {
        var audio = new FakeAudioPlayer();
        var service = CreateService(audio);
        var first = Track("first.mp3");
        var second = Track("second.mp3");
        service.Play([first, second]);

        audio.RaisePosition(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(59.9));
        Assert.True(SpinWait.SpinUntil(
            () => service.Current?.FilePath == second.FilePath,
            TimeSpan.FromSeconds(2)));

        Assert.Equal(second.FilePath, Assert.Single(audio.Crossfaded).Path);
    }

    [Fact]
    public void MissingTrack_IsSkippedWithVisibleIssue()
    {
        var audio = new FakeAudioPlayer();
        var service = CreateService(audio);
        var missing = Track("missing.mp3");
        var available = Track("available.mp3");
        File.Delete(missing.FilePath);

        service.Play([missing, available]);

        Assert.Equal(available.FilePath, service.Current?.FilePath);
        Assert.Equal(available.FilePath, Assert.Single(audio.Played).Path);
        Assert.Contains("übersprungen", service.Issue?.Message);
        Assert.False(service.Issue?.CanRetry);
    }

    [Fact]
    public void DecoderFailure_SkipsToNextTrack()
    {
        var audio = new FakeAudioPlayer();
        var service = CreateService(audio);
        var broken = Track("broken.mp3");
        var available = Track("available.mp3");
        service.Play([broken, available]);

        audio.RaiseFailure(broken.FilePath);
        Assert.True(SpinWait.SpinUntil(
            () => service.Current?.FilePath == available.FilePath,
            TimeSpan.FromSeconds(2)));

        Assert.Contains("konnte nicht abgespielt", service.Issue?.Message);
        Assert.Equal(available.FilePath, audio.Played.Last().Path);
    }

    [Fact]
    public void RadioFailure_CanBeRetried()
    {
        var audio = new FakeAudioPlayer();
        var service = CreateService(audio);
        var station = new RadioStation
        {
            StationUuid = "radio",
            Name = "Test Radio",
            ResolvedUrl = "https://example.test/stream"
        };
        service.PlayRadio(station);

        audio.RaiseFailure(station.StreamUrl);
        Assert.True(SpinWait.SpinUntil(() => service.Issue is not null, TimeSpan.FromSeconds(2)));
        Assert.True(service.Issue?.CanRetry);

        service.Retry();
        Assert.Null(service.Issue);
        Assert.Equal(2, audio.PlayedLocations.Count);
    }

    private PlaybackService CreateService(FakeAudioPlayer audio) => new(
        audio,
        new PlaybackSessionStore(SessionPath),
        new UserMusicDataService(UserDataPath),
        new PlaybackPreferencesService(PreferencesPath));

    private Track Track(string name)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "");
        return new Track
        {
            FilePath = path,
            Title = Path.GetFileNameWithoutExtension(name),
            Artist = "Artist",
            Album = "Album",
            Duration = TimeSpan.FromMinutes(3)
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class FakeAudioPlayer : IAudioPlayerService
    {
        public event EventHandler? StateChanged;
        public event EventHandler<TimeSpan>? PositionChanged;
        public event EventHandler? TrackEnded;
        public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

        public List<(string Path, TimeSpan? StartPosition)> Played { get; } = [];
        public List<(string Path, TimeSpan Duration)> Crossfaded { get; } = [];
        public List<string> PlayedLocations { get; } = [];
        public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; private set; }
        public TimeSpan Duration { get; private set; } = TimeSpan.FromMinutes(3);
        public int Volume { get; set; } = 100;

        public void Play(string filePath, TimeSpan? startPosition = null)
        {
            Played.Add((filePath, startPosition));
            Position = startPosition ?? TimeSpan.Zero;
            IsPlaying = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
            PositionChanged?.Invoke(this, Position);
        }

        public void PlayLocation(string url)
        {
            PlayedLocations.Add(url);
            IsPlaying = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void PlayVideo(VideoItem video) => PlayLocation(video.PlaybackLocation);

        public Task<bool> CrossfadeToAsync(
            string filePath,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            Crossfaded.Add((filePath, duration));
            Position = TimeSpan.Zero;
            IsPlaying = true;
            return Task.FromResult(true);
        }

        public void CancelTransition() { }

        public void TogglePlayPause()
        {
            IsPlaying = !IsPlaying;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Stop()
        {
            IsPlaying = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Seek(TimeSpan position)
        {
            Position = position;
            PositionChanged?.Invoke(this, position);
        }

        public void RaiseEnded() => TrackEnded?.Invoke(this, EventArgs.Empty);
        public void RaiseFailure(string source) =>
            PlaybackFailed?.Invoke(this, new PlaybackFailedEventArgs(source));
        public void RaisePosition(TimeSpan position)
        {
            Position = position;
            PositionChanged?.Invoke(this, position);
        }
    }
}
