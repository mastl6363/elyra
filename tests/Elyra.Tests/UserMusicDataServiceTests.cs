using Elyra.Models;
using Elyra.Services;

namespace Elyra.Tests;

public sealed class UserMusicDataServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elyra-user-music-{Guid.NewGuid():N}");
    private string StatePath => Path.Combine(_directory, "user-music.json");

    [Fact]
    public void FavoritesAndHistory_PersistAcrossInstances()
    {
        var track = CreateTrack("favorite.mp3");
        var service = new UserMusicDataService(StatePath);

        service.ToggleFavorite(track);
        service.RecordPlayed(track);
        service.RecordPlayed(track);

        var restored = new UserMusicDataService(StatePath);
        Assert.True(restored.IsFavorite(track));
        var history = Assert.Single(restored.History);
        Assert.Equal(2, history.PlayCount);
        Assert.Equal(track.FilePath, history.Track.FilePath);
    }

    [Fact]
    public void ResolveFavorites_UsesCurrentLibraryMetadata()
    {
        var original = CreateTrack("song.mp3", "Old title");
        var service = new UserMusicDataService(StatePath);
        service.ToggleFavorite(original);
        var updated = CreateTrack("song.mp3", "Updated title");

        var resolved = Assert.Single(service.ResolveFavorites([updated]));

        Assert.Equal("Updated title", resolved.Title);
    }

    private Track CreateTrack(string name, string title = "Song")
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "");
        return new Track
        {
            FilePath = path,
            Title = title,
            Artist = "Artist",
            Album = "Album",
            Duration = TimeSpan.FromMinutes(3)
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
