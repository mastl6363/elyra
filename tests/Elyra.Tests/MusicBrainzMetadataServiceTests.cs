using System.Net;
using System.Text;
using Elyra.Models;
using Elyra.Services;

namespace Elyra.Tests;

public sealed class MusicBrainzMetadataServiceTests
{
    [Fact]
    public async Task EnrichMissingAlbumsAsync_ReturnsConservativeAlbumMatch()
    {
        var handler = new StubHandler(AlbumResponse("Discovery", "Album", 100));
        var service = CreateService(handler);

        var result = await service.EnrichMissingAlbumsAsync([Track("One More Time", "Daft Punk")]);

        var update = Assert.Single(result.Updates);
        Assert.Equal("Discovery", update.Album);
        Assert.Equal("Daft Punk", update.AlbumArtist);
        Assert.Equal(1, result.Matched);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public async Task EnrichMissingAlbumsAsync_QueriesDuplicateArtistAndTitleOnlyOnce()
    {
        var handler = new StubHandler(AlbumResponse("Discovery", "Album", 100));
        var service = CreateService(handler);
        var first = Track("One More Time", "Daft Punk", "first.mp3");
        var second = Track("One More Time", "Daft Punk", "second.mp3");

        var result = await service.EnrichMissingAlbumsAsync([first, second]);

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(2, result.Updates.Count);
        Assert.Equal(2, result.Matched);
    }

    [Theory]
    [InlineData("Single", 100)]
    [InlineData("Album", 70)]
    public async Task EnrichMissingAlbumsAsync_SkipsUnsafeMatches(string releaseType, int score)
    {
        var service = CreateService(new StubHandler(AlbumResponse("One More Time", releaseType, score)));

        var result = await service.EnrichMissingAlbumsAsync([Track("One More Time", "Daft Punk")]);

        Assert.Empty(result.Updates);
        Assert.Equal(1, result.Skipped);
    }

    private static MusicBrainzMetadataService CreateService(StubHandler handler) =>
        new(new HttpClient(handler), _ => Task.CompletedTask);

    private static Track Track(string title, string artist, string fileName = "song.mp3") => new()
    {
        FilePath = $@"C:\Music\{fileName}",
        Title = title,
        Artist = artist,
        Album = "",
        Duration = TimeSpan.FromMilliseconds(320746)
    };

    private static string AlbumResponse(string album, string releaseType, int score) => $$"""
        {
          "recordings": [
            {
              "score": {{score}},
              "title": "One More Time",
              "length": 320746,
              "artist-credit": [{ "name": "Daft Punk", "artist": { "name": "Daft Punk" } }],
              "releases": [
                {
                  "title": "{{album}}",
                  "status": "Official",
                  "date": "2001-03-12",
                  "release-group": {
                    "id": "a1111111-1111-1111-1111-111111111111",
                    "title": "{{album}}",
                    "primary-type": "{{releaseType}}",
                    "secondary-types": []
                  }
                }
              ]
            }
          ]
        }
        """;

    private sealed class StubHandler(string responseBody) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
