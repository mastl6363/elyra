using Elyra.Models;
using Elyra.Services;

namespace Elyra.Tests;

public sealed class MusicLibraryServiceTests
{
    [Fact]
    public void Constructor_RestoresSavedFolderAndTracks()
    {
        var store = new InMemoryStateStore
        {
            State = new LibraryState
            {
                FolderPath = @"C:\Music",
                Tracks = [Track("Saved song")]
            }
        };

        var library = new MusicLibraryService(store);

        Assert.Equal(@"C:\Music", library.FolderPath);
        Assert.Equal("Saved song", Assert.Single(library.Tracks).Title);
        Assert.Single(library.Albums);
    }

    [Fact]
    public void Clear_ResetsLibraryAndDeletesSnapshot()
    {
        var store = new InMemoryStateStore
        {
            State = new LibraryState { FolderPath = @"C:\Music", Tracks = [Track("Song")] }
        };
        var library = new MusicLibraryService(store);
        var changed = 0;
        library.Changed += (_, _) => changed++;

        library.Clear();

        Assert.Null(library.FolderPath);
        Assert.Empty(library.Tracks);
        Assert.True(store.WasCleared);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Artists_GroupAllSongsByArtistAndKeepMissingAlbumEmpty()
    {
        var store = new InMemoryStateStore
        {
            State = new LibraryState
            {
                Tracks =
                [
                    Track("First", "Artist A", "Album A"),
                    Track("Second", "Artist A", ""),
                    Track("Third", "Artist B", "")
                ]
            }
        };

        var library = new MusicLibraryService(store);

        Assert.Equal(2, library.Artists.Count);
        var artistA = library.Artists.Single(artist => artist.Name == "Artist A");
        Assert.Equal(2, artistA.TrackCount);
        Assert.Single(artistA.Albums);
        Assert.Single(library.Albums);
    }

    [Fact]
    public void Constructor_MigratesLegacyUnknownAlbumPlaceholder()
    {
        var store = new InMemoryStateStore
        {
            State = new LibraryState { Tracks = [Track("Song", "Artist", "Unbekanntes Album")] }
        };

        var library = new MusicLibraryService(store);

        Assert.Equal("", Assert.Single(library.Tracks).Album);
        Assert.Empty(library.Albums);
        Assert.Equal("", Assert.Single(store.State!.Tracks).Album);
    }

    [Fact]
    public void ApplyMetadataUpdates_UpdatesOnlyMissingAlbumsAndPersistsSnapshot()
    {
        var missing = Track("Missing", "Artist", "");
        var existing = Track("Existing", "Artist", "Original Album");
        var store = new InMemoryStateStore
        {
            State = new LibraryState { Tracks = [missing, existing] }
        };
        var library = new MusicLibraryService(store);

        var count = library.ApplyMetadataUpdates(
        [
            new TrackMetadataUpdate(missing.FilePath, "Matched Album", "Artist"),
            new TrackMetadataUpdate(existing.FilePath, "Wrong Album", "Artist")
        ]);

        Assert.Equal(1, count);
        Assert.Equal("Matched Album", library.Tracks.Single(track => track.Title == "Missing").Album);
        Assert.Equal("Original Album", library.Tracks.Single(track => track.Title == "Existing").Album);
        Assert.Equal("Matched Album", store.State!.Tracks.Single(track => track.Title == "Missing").Album);
    }

    private static Track Track(string title, string artist = "Artist", string album = "Album") => new()
    {
        FilePath = $@"C:\Music\{title}.mp3",
        Title = title,
        Artist = artist,
        Album = album
    };

    private sealed class InMemoryStateStore : ILibraryStateStore
    {
        public LibraryState? State { get; set; }
        public bool WasCleared { get; private set; }

        public LibraryState? Load() => State;
        public void Save(LibraryState state) => State = state;
        public void Clear()
        {
            State = null;
            WasCleared = true;
        }
    }
}
