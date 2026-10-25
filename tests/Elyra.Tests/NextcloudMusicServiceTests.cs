using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Elyra.Models;
using Elyra.Services;

namespace Elyra.Tests;

public sealed class NextcloudMusicServiceTests
{
    [Fact]
    public async Task ScanAndStream_UsesWebDavAndForwardsRangeWithoutExposingCredentials()
    {
        var directory = Path.Combine(Path.GetTempPath(), "elyra-nextcloud-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var service = new NextcloudMusicService(new HttpClient(new WebDavHandler()),
                Path.Combine(directory, "settings.json"), Path.Combine(directory, "cache"));
            var tracks = await service.ConnectAndScanAsync(new NextcloudConnection
            {
                ServerUrl = "https://cloud.example.test/login",
                Username = "alice",
                MusicFolder = "Music"
            }, "private-app-password");
            var track = Assert.Single(tracks, t => t.NextcloudPath!.EndsWith(".mp3"));
            Assert.Contains(tracks, t => t.NextcloudPath!.EndsWith(".ogg"));
            Assert.Equal("Artist", track.Artist);
            Assert.Equal("Album", track.Album);
            Assert.Equal("Song", track.Title);
            Assert.Equal("Music/Artist/Album/Song.mp3", track.NextcloudPath);
            Assert.DoesNotContain("private-app-password", File.ReadAllText(Path.Combine(directory, "settings.json")));
            var location = service.GetPlaybackLocation(track);
            Assert.StartsWith("http://127.0.0.1:", location);
            Assert.DoesNotContain("private-app-password", location);
            using var player = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, location);
            request.Headers.Range = new RangeHeaderValue(2, 4);
            using var response = await player.SendAsync(request);
            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal("cde", await response.Content.ReadAsStringAsync());
            Assert.Equal("bytes 2-4/6", response.Content.Headers.ContentRange?.ToString());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (!service.IsCached(track))
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Delay(20, timeout.Token);
            }
            Assert.Equal("abcdef", File.ReadAllText(service.GetPlaybackLocation(track)));
            using var restarted = new NextcloudMusicService(new HttpClient(new WebDavHandler()),
                Path.Combine(directory, "settings.json"), Path.Combine(directory, "cache"));
            Assert.False(restarted.IsConnected);
            Assert.Equal("abcdef", File.ReadAllText(restarted.GetPlaybackLocation(track)));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task LocalRescanPreservesNextcloudTracks()
    {
        var store = new InMemoryStateStore();
        var library = new MusicLibraryService(store);
        library.SetNextcloudTracks([new Track
        {
            FilePath = "nextcloud:one", NextcloudPath = "Music/Song.mp3",
            Title = "Song", Artist = "Artist", Album = "Album"
        }]);
        var folder = Path.Combine(Path.GetTempPath(), "elyra-empty-music-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await library.ImportFolderAsync(folder);
            Assert.Single(library.Tracks);
            Assert.Equal("Music/Song.mp3", library.Tracks[0].NextcloudPath);
        }
        finally { Directory.Delete(folder); }
    }

    private sealed class InMemoryStateStore : ILibraryStateStore
    {
        private LibraryState? _state;
        public LibraryState? Load() => _state;
        public void Save(LibraryState state) => _state = state;
        public void Clear() => _state = null;
    }

    private sealed class WebDavHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
            Assert.Equal("alice:private-app-password", Encoding.UTF8.GetString(
                Convert.FromBase64String(request.Headers.Authorization!.Parameter!)));
            Assert.Equal("cloud.example.test", request.RequestUri?.Host);
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            if (request.Method.Method == "PROPFIND")
            {
                var folder = path.TrimEnd('/');
                string[] children = folder.EndsWith("/Music") ? [folder + "/Artist/"] :
                    folder.EndsWith("/Artist") ? [folder + "/Album/"] :
                    folder.EndsWith("/Album") ? [folder + "/Song.mp3", folder + "/Tone.ogg"] : [];
                var responses = string.Join("", children.Select(child =>
                    $"<d:response><d:href>{child}</d:href><d:propstat><d:status>HTTP/1.1 200 OK</d:status><d:prop><d:resourcetype>{(child.EndsWith('/') ? "<d:collection/>" : "")}</d:resourcetype><d:getetag>\"etag1\"</d:getetag></d:prop></d:propstat></d:response>"));
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)207)
                {
                    Content = new StringContent($"<d:multistatus xmlns:d=\"DAV:\">{responses}</d:multistatus>")
                });
            }
            Assert.EndsWith("/Music/Artist/Album/Song.mp3", path);
            var full = "abcdef";
            var range = request.Headers.Range?.Ranges.FirstOrDefault();
            var start = (int)(range?.From ?? 0);
            var end = (int)(range?.To ?? 5);
            var response = new HttpResponseMessage(range is null ? HttpStatusCode.OK : HttpStatusCode.PartialContent)
            { Content = new StringContent(full[start..(end + 1)]) };
            if (range is not null) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, 6);
            return Task.FromResult(response);
        }
    }
}
