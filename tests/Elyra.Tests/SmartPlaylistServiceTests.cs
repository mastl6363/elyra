using Elyra.Models;
using Elyra.Services;

namespace Elyra.Tests;

public sealed class SmartPlaylistServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elyra-smart-{Guid.NewGuid():N}");

    [Fact]
    public void GetAll_BuildsHistoryFormatAndGenreCollections()
    {
        Directory.CreateDirectory(_directory);
        var tracks = new[]
        {
            Track("Played", "played.mp3", "Rock"),
            Track("Lossless", "lossless.flac", "Rock"),
            Track("Third", "third.mp3", "Rock"),
            Track("Fourth", "fourth.mp3", "Rock"),
            Track("Fifth", "fifth.mp3", "Rock")
        };
        using var library = new MusicLibraryService(new StubStore(new LibraryState
        {
            MetadataVersion = 3,
            Tracks = tracks.ToList()
        }));
        var userMusic = new UserMusicDataService(Path.Combine(_directory, "user-music.json"));
        userMusic.RecordPlayed(tracks[0]);

        var playlists = new SmartPlaylistService(library, userMusic).GetAll();

        var unplayed = Assert.Single(playlists, playlist => playlist.Id == "unplayed");
        Assert.DoesNotContain(unplayed.Tracks, track => track.FilePath == tracks[0].FilePath);
        Assert.Equal(4, unplayed.Tracks.Count);

        var rediscover = Assert.Single(playlists, playlist => playlist.Id == "rediscover");
        Assert.Collection(rediscover.Tracks, track => Assert.Equal(tracks[0].FilePath, track.FilePath));

        var lossless = Assert.Single(playlists, playlist => playlist.Id == "lossless");
        Assert.Collection(lossless.Tracks, track => Assert.Equal("Lossless", track.Title));

        var genre = Assert.Single(playlists, playlist => playlist.Name == "Rock Mix");
        Assert.Equal(5, genre.Tracks.Count);
    }

    private Track Track(string title, string fileName, string genre) => new()
    {
        FilePath = Path.Combine(_directory, fileName),
        Title = title,
        Artist = "Artist",
        Album = "",
        Genre = genre,
        Duration = TimeSpan.FromMinutes(3)
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    private sealed class StubStore(LibraryState state) : ILibraryStateStore
    {
        public LibraryState? Load() => state;
        public void Save(LibraryState value) { }
        public void Clear() { }
    }
}
