using Elyra.Models;
using Elyra.Services;

namespace Elyra.Tests;

public sealed class LibraryFilterTests
{
    private readonly IReadOnlyList<Album> _albums =
    [
        Album("Discovery", "Daft Punk", Track("One More Time", "Daft Punk", "Discovery", "track.mp3")),
        Album("Kind of Blue", "Miles Davis", Track("So What", "Miles Davis", "Kind of Blue", "track.flac"))
    ];

    [Theory]
    [InlineData("discovery", "Discovery")]
    [InlineData("miles", "Kind of Blue")]
    [InlineData("one more", "Discovery")]
    public void Apply_SearchesAlbumArtistAndTrack(string query, string expectedAlbum)
    {
        var result = LibraryFilter.Apply(_albums, query, AudioFileFilter.All);

        Assert.Collection(result, album => Assert.Equal(expectedAlbum, album.Title));
    }

    [Fact]
    public void Apply_FiltersByFileTypeCaseInsensitively()
    {
        var result = LibraryFilter.Apply(_albums, null, AudioFileFilter.Flac);

        Assert.Collection(result, album => Assert.Equal("Kind of Blue", album.Title));
    }

    [Fact]
    public void Apply_ReturnsNoAlbumsWhenSearchDoesNotMatch()
    {
        Assert.Empty(LibraryFilter.Apply(_albums, "not in library", AudioFileFilter.All));
    }

    [Fact]
    public void Apply_ArtistsSearchesSongAndAlbumButReturnsArtist()
    {
        var artists = new[]
        {
            Artist("Daft Punk", Track("One More Time", "Daft Punk", "Discovery", "track.mp3")),
            Artist("Miles Davis", Track("So What", "Miles Davis", "Kind of Blue", "track.flac"))
        };

        var result = LibraryFilter.Apply(artists, "kind of blue", AudioFileFilter.All);

        Assert.Equal("Miles Davis", Assert.Single(result).Name);
    }

    [Fact]
    public void Apply_ArtistsHonorsFormatFilter()
    {
        var artists = new[]
        {
            Artist("Daft Punk", Track("One More Time", "Daft Punk", "Discovery", "track.mp3")),
            Artist("Miles Davis", Track("So What", "Miles Davis", "Kind of Blue", "track.flac"))
        };

        Assert.Equal("Daft Punk", Assert.Single(LibraryFilter.Apply(artists, null, AudioFileFilter.Mp3)).Name);
    }

    private static Album Album(string title, string artist, params Track[] tracks) => new()
    {
        Id = title,
        Title = title,
        Artist = artist,
        Tracks = tracks
    };

    private static Artist Artist(string name, params Track[] tracks) => new()
    {
        Id = name,
        Name = name,
        Tracks = tracks,
        Albums = []
    };

    private static Track Track(string title, string artist, string album, string filePath) => new()
    {
        FilePath = filePath,
        Title = title,
        Artist = artist,
        Album = album,
        Duration = TimeSpan.FromMinutes(3)
    };
}
