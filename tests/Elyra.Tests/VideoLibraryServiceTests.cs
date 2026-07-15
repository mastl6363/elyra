using Elyra.Models;
using Elyra.Services;

namespace Elyra.Tests;

public sealed class VideoLibraryServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elyra-video-tests-{Guid.NewGuid():N}");
    private string StatePath => Path.Combine(_directory, "videos.json");

    [Fact]
    public void AddFiles_FiltersUnsupportedFilesAndAvoidsDuplicates()
    {
        Directory.CreateDirectory(_directory);
        var movie = Path.Combine(_directory, "My.Movie.mkv");
        var text = Path.Combine(_directory, "notes.txt");
        File.WriteAllText(movie, "");
        File.WriteAllText(text, "");
        var service = new VideoLibraryService(StatePath);

        var added = service.AddFiles([movie, movie, text]);

        Assert.Equal(1, added);
        var video = Assert.Single(service.Videos);
        Assert.Equal("My Movie", video.Title);
        Assert.Equal(VideoSourceKind.File, video.Kind);
    }

    [Fact]
    public void SavePosition_PersistsResumePointAndResetsNearEnd()
    {
        Directory.CreateDirectory(_directory);
        var movie = Path.Combine(_directory, "movie.mp4");
        File.WriteAllText(movie, "");
        var service = new VideoLibraryService(StatePath);
        service.AddFiles([movie]);
        var id = Assert.Single(service.Videos).Id;

        service.SavePosition(id, TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(90));
        var restored = new VideoLibraryService(StatePath);
        Assert.Equal(TimeSpan.FromMinutes(12), Assert.Single(restored.Videos).SavedPosition);

        restored.SavePosition(id, TimeSpan.FromMinutes(89.75), TimeSpan.FromMinutes(90));
        Assert.Equal(TimeSpan.Zero, Assert.Single(new VideoLibraryService(StatePath).Videos).SavedPosition);
    }

    [Fact]
    public void BuildDvdLocation_CreatesVlcMediaLocation()
    {
        Assert.Equal("dvd:///D:/", VideoItem.BuildDvdLocation(@"D:\"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
