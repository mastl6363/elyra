using Elyra.Models;
using Elyra.Services;

namespace Elyra.Tests;

public sealed class LibraryHealthServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elyra-health-{Guid.NewGuid():N}");

    [Fact]
    public async Task Analyze_FindsMissingFilesAndMetadataDuplicates()
    {
        Directory.CreateDirectory(_directory);
        var firstPath = Path.Combine(_directory, "first.mp3");
        var secondPath = Path.Combine(_directory, "second.mp3");
        File.WriteAllText(firstPath, "");
        File.WriteAllText(secondPath, "");
        var tracks = new[]
        {
            Track(firstPath, "Same"),
            Track(secondPath, "Same"),
            Track(Path.Combine(_directory, "missing.mp3"), "Missing")
        };

        var report = await new LibraryHealthService().AnalyzeAsync(tracks);

        Assert.Single(report.MissingFiles);
        Assert.Equal(2, Assert.Single(report.PossibleDuplicates).Tracks.Count);
    }

    private static Track Track(string path, string title) => new()
    {
        FilePath = path,
        Title = title,
        Artist = "Artist",
        Album = "",
        Duration = TimeSpan.FromMinutes(3)
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
