using Elyra.Models;
using Elyra.Services;

namespace Elyra.Tests;

public sealed class SqliteLibraryStateStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elyra-sqlite-{Guid.NewGuid():N}");

    [Fact]
    public void SaveAndLoad_RoundTripsIndexedMetadata()
    {
        Directory.CreateDirectory(_directory);
        using var store = new SqliteLibraryStateStore(Path.Combine(_directory, "library.db3"));
        store.Save(new LibraryState
        {
            MetadataVersion = 3,
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
                    Year = 2024,
                    Duration = TimeSpan.FromSeconds(123)
                }
            ]
        });

        var restored = store.Load();

        Assert.NotNull(restored);
        Assert.Equal(3, restored.MetadataVersion);
        var track = Assert.Single(restored.Tracks);
        Assert.Equal("Electronic", track.Genre);
        Assert.Equal((uint)2024, track.Year);
        Assert.Equal(TimeSpan.FromSeconds(123), track.Duration);
    }

    [Fact]
    public void Load_ImportsLegacySnapshotOnce()
    {
        Directory.CreateDirectory(_directory);
        var legacy = new StubStore
        {
            State = new LibraryState
            {
                FolderPath = @"C:\Legacy",
                Tracks = [Track("Legacy")]
            }
        };

        using (var store = new SqliteLibraryStateStore(Path.Combine(_directory, "library.db3"), legacy))
            Assert.Equal("Legacy", Assert.Single(store.Load()!.Tracks).Title);

        legacy.State = null;
        using var reopened = new SqliteLibraryStateStore(Path.Combine(_directory, "library.db3"), legacy);
        Assert.Equal("Legacy", Assert.Single(reopened.Load()!.Tracks).Title);
    }

    private static Track Track(string title) => new()
    {
        FilePath = $@"C:\Music\{title}.mp3",
        Title = title,
        Artist = "Artist",
        Album = ""
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class StubStore : ILibraryStateStore
    {
        public LibraryState? State { get; set; }
        public LibraryState? Load() => State;
        public void Save(LibraryState state) => State = state;
        public void Clear() => State = null;
    }
}
