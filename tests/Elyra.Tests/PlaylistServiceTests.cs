using Elyra.Models;
using Elyra.Services;

namespace Elyra.Tests;

public sealed class PlaylistServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elyra-playlist-tests-{Guid.NewGuid():N}");
    private string FilePath => Path.Combine(_directory, "playlists.json");

    [Fact]
    public void AddTrack_TreatsDifferentPathCasingAsTheSameFile()
    {
        var service = new PlaylistService(FilePath);
        var playlist = service.Create("Favorites");
        service.AddTrack(playlist.Id, Track(@"C:\Music\Song.mp3"));

        service.AddTrack(playlist.Id, Track(@"c:\music\song.mp3"));

        Assert.Single(service.Find(playlist.Id)!.Entries);
    }

    [Fact]
    public void RemoveTrack_MatchesRegardlessOfPathCasing()
    {
        var service = new PlaylistService(FilePath);
        var playlist = service.Create("Favorites");
        service.AddTrack(playlist.Id, Track(@"C:\Music\Song.mp3"));

        service.RemoveTrack(playlist.Id, @"c:\music\song.mp3");

        Assert.Empty(service.Find(playlist.Id)!.Entries);
    }

    [Fact]
    public void AddTrack_PersistsAcrossInstances()
    {
        var service = new PlaylistService(FilePath);
        var playlist = service.Create("Favorites");
        service.AddTrack(playlist.Id, Track(@"C:\Music\Song.mp3"));

        var restored = new PlaylistService(FilePath);

        Assert.Single(restored.Find(playlist.Id)!.Entries);
    }

    private static Track Track(string filePath) => new()
    {
        FilePath = filePath,
        Title = "Song",
        Artist = "Artist",
        Album = "Album"
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
