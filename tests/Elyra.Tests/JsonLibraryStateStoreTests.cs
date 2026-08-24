using Elyra.Models;
using Elyra.Services;

namespace Elyra.Tests;

public sealed class JsonLibraryStateStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elyra-tests-{Guid.NewGuid():N}");

    [Fact]
    public void SaveAndLoad_RoundTripsLibrarySnapshot()
    {
        var store = CreateStore();
        var state = new LibraryState
        {
            MetadataVersion = 2,
            FolderPath = @"C:\Music",
            Tracks =
            [
                new Track
                {
                    FilePath = @"C:\Music\song.flac",
                    Title = "Song",
                    Artist = "Artist",
                    Album = "Album",
                    Genre = "Electronic",
                    Duration = TimeSpan.FromSeconds(125),
                    CoverArtDataUri = "data:image/png;base64,AA=="
                }
            ]
        };

        store.Save(state);
        var restored = store.Load();

        Assert.NotNull(restored);
        Assert.Equal(2, restored.MetadataVersion);
        Assert.Equal(state.FolderPath, restored.FolderPath);
        var track = Assert.Single(restored.Tracks);
        Assert.Equal("Song", track.Title);
        Assert.Equal("Electronic", track.Genre);
        Assert.Equal(TimeSpan.FromSeconds(125), track.Duration);
        Assert.Equal("data:image/png;base64,AA==", track.CoverArtDataUri);
    }

    [Fact]
    public void Clear_RemovesPersistedSnapshot()
    {
        var store = CreateStore();
        store.Save(new LibraryState { FolderPath = @"C:\Music" });

        store.Clear();

        Assert.Null(store.Load());
    }

    [Fact]
    public void Load_ReturnsNullForCorruptSnapshot()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "library.json"), "not-json");

        Assert.Null(CreateStore().Load());
    }

    private JsonLibraryStateStore CreateStore() =>
        new(Path.Combine(_directory, "library.json"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }
}
