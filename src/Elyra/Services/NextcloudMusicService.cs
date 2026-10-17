using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Elyra.Models;

namespace Elyra.Services;

/// <summary>Nextcloud WebDAV discovery and authenticated, on-demand audio streaming.</summary>
public sealed class NextcloudMusicService : IDisposable
{
    private readonly HttpClient _http;
    private readonly MusicLibraryService? _library;
    private readonly string _settingsPath;
    private readonly string _cacheDirectory;
    private readonly object _sync = new();
    private readonly Dictionary<string, Track> _streamTracks = new();
    private HttpListener? _listener;
    private int _port;
    private readonly string _streamToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private string? _appPassword;
    private NextcloudConnection _connection = new();
    private const long MaxCacheBytes = 1024L * 1024 * 1024;

    public NextcloudMusicService(MusicLibraryService library) : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }),
        Path.Combine(AppPaths.DataDirectory, "nextcloud.json"),
        Path.Combine(AppPaths.DataDirectory, "nextcloud-cache"), library) { }

    public NextcloudMusicService(HttpClient http, string settingsPath, string cacheDirectory, MusicLibraryService? library = null)
    {
        _http = http;
        _library = library;
        _settingsPath = settingsPath;
        _cacheDirectory = cacheDirectory;
        try
        {
            if (File.Exists(_settingsPath))
                _connection = JsonSerializer.Deserialize<NextcloudConnection>(File.ReadAllText(_settingsPath)) ?? new();
        }
        catch { /* malformed preferences must not prevent startup */ }
    }

    public NextcloudConnection Connection => _connection;
    public bool IsConnected => !string.IsNullOrEmpty(_appPassword);

    public async Task<IReadOnlyList<Track>> ConnectAndScanAsync(NextcloudConnection connection,
        string appPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connection.Username) || string.IsNullOrWhiteSpace(appPassword))
            throw new ArgumentException("Benutzername und App-Passwort sind erforderlich.");
        var normalized = Normalize(connection);
        var previousConnection = _connection;
        var previousPassword = _appPassword;
        _connection = normalized;
        _appPassword = appPassword;
        try
        {
            var tracks = await ScanAsync(cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(normalized));
            return tracks.Select(track =>
            {
                if (!IsCached(track)) return track;
                try { return MusicLibraryService.ReadCachedNextcloudTrack(track, CachePath(track)); }
                catch { return track; }
            }).ToList();
        }
        catch
        {
            _connection = previousConnection;
            _appPassword = previousPassword;
            throw;
        }
    }

    public void Disconnect()
    {
        _appPassword = null;
        _connection = new();
        lock (_sync) _streamTracks.Clear();
        try { File.Delete(_settingsPath); } catch { }
    }

    public async Task<IReadOnlyList<Track>> ScanAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        var tracks = new List<Track>();
        var folders = new Queue<string>();
        folders.Enqueue(_connection.MusicFolder);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (folders.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = folders.Dequeue();
            if (!visited.Add(folder)) continue;
            if (visited.Count > 10000) throw new IOException("Zu viele Nextcloud-Unterordner.");
            using var request = CreateRequest(new HttpMethod("PROPFIND"), folder, collection: true);
            request.Headers.Add("Depth", "1");
            request.Content = new StringContent("<d:propfind xmlns:d=\"DAV:\"><d:prop><d:resourcetype/><d:getetag/></d:prop></d:propfind>", Encoding.UTF8, "application/xml");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                throw new UnauthorizedAccessException("Nextcloud hat die Anmeldung abgelehnt. Bitte App-Passwort prüfen.");
            response.EnsureSuccessStatusCode();
            var xml = XDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            XNamespace d = "DAV:";
            var baseUri = CreateFileUri(folder, collection: true);
            foreach (var item in xml.Descendants(d + "response"))
            {
                var href = item.Element(d + "href")?.Value;
                if (string.IsNullOrWhiteSpace(href)) continue;
                var uri = new Uri(baseUri, href);
                if (!baseUri.IsBaseOf(uri) || uri == baseUri) continue;
                var relative = Uri.UnescapeDataString(uri.AbsolutePath[baseUri.AbsolutePath.Length..]).TrimEnd('/');
                if (relative.Length == 0 || relative.Contains("../", StringComparison.Ordinal)) continue;
                var path = folder.Length == 0 ? relative : $"{folder.TrimEnd('/')}/{relative}";
                var prop = item.Elements(d + "propstat")
                    .Where(p => p.Element(d + "status")?.Value.Contains(" 200 ") == true)
                    .Select(p => p.Element(d + "prop")).FirstOrDefault(p => p is not null);
                if (prop is null) continue;
                if (prop.Element(d + "resourcetype")?.Element(d + "collection") is not null)
                {
                    folders.Enqueue(path);
                    continue;
                }
                var ext = Path.GetExtension(path);
                if (!ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".flac", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var title = Path.GetFileNameWithoutExtension(path);
                var album = parts.Length >= 2 ? parts[^2] : "";
                var artist = parts.Length >= 3 ? parts[^3] : "Unbekannter Künstler";
                tracks.Add(new Track
                {
                    FilePath = "nextcloud:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_connection.ServerUrl + "\0" + _connection.Username + "\0" + path))),
                    NextcloudPath = path,
                    NextcloudEtag = prop.Element(d + "getetag")?.Value,
                    Title = title,
                    Album = album,
                    Artist = artist
                });
            }
        }
        return tracks;
    }

    /// <summary>Returns a complete cached file or an authenticated loopback stream.</summary>
    public string GetPlaybackLocation(Track track)
    {
        if (track.NextcloudPath is null) throw new ArgumentException("Kein Nextcloud-Titel.");
        var cached = CachePath(track);
        if (File.Exists(cached))
        {
            File.SetLastAccessTimeUtc(cached, DateTime.UtcNow);
            return cached;
        }
        EnsureConnected();
        StartStreamServer();
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        lock (_sync) _streamTracks[id] = track;
        _ = Task.Run(() => CacheAsync(track));
        return $"http://127.0.0.1:{_port}/{_streamToken}/{id}";
    }

    public bool IsCached(Track track) => track.NextcloudPath is not null && File.Exists(CachePath(track));

    private async Task CacheAsync(Track track)
    {
        var destination = CachePath(track);
        if (File.Exists(destination)) return;
        Directory.CreateDirectory(_cacheDirectory);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var request = CreateRequest(HttpMethod.Get, track.NextcloudPath!);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.StatusCode != HttpStatusCode.OK) return;
            await using (var file = File.Create(temporary))
                await response.Content.CopyToAsync(file);
            File.Move(temporary, destination, true);
            _library?.ApplyCachedNextcloudTrack(track, destination);
            PruneCache();
        }
        catch { /* streaming can continue even if the cache write fails */ }
        finally { try { File.Delete(temporary); } catch { } }
    }

    private void PruneCache()
    {
        try
        {
            var files = new DirectoryInfo(_cacheDirectory).EnumerateFiles()
                .Where(f => !f.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.LastAccessTimeUtc).ToList();
            var total = files.Sum(f => f.Length);
            foreach (var file in files)
            {
                if (total <= MaxCacheBytes) break;
                total -= file.Length;
                file.Delete();
            }
        }
        catch { }
    }

    private string CachePath(Track track)
    {
        var key = $"{_connection.ServerUrl}\0{_connection.Username}\0{track.NextcloudPath}\0{track.NextcloudEtag}";
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        var ext = Path.GetExtension(track.NextcloudPath);
        return Path.Combine(_cacheDirectory, name + ext);
    }

    private void StartStreamServer()
    {
        lock (_sync)
        {
            if (_listener is not null) return;
            var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            _port = ((IPEndPoint)socket.LocalEndpoint).Port;
            socket.Stop();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            listener.Start();
            _listener = listener;
            _ = Task.Run(async () =>
            {
                while (listener.IsListening)
                {
                    HttpListenerContext context;
                    try { context = await listener.GetContextAsync(); }
                    catch { break; }
                    _ = Task.Run(() => ServeAsync(context));
                }
            });
        }
    }

    private async Task ServeAsync(HttpListenerContext context)
    {
        try
        {
            var segments = context.Request.Url?.AbsolutePath.Trim('/').Split('/');
            if (segments is not { Length: 2 } || segments[0] != _streamToken ||
                (context.Request.HttpMethod != "GET" && context.Request.HttpMethod != "HEAD"))
            { context.Response.StatusCode = 404; return; }
            Track? track;
            lock (_sync) _streamTracks.TryGetValue(segments[1], out track);
            if (track is null || !IsConnected) { context.Response.StatusCode = 404; return; }
            using var request = CreateRequest(HttpMethod.Get, track.NextcloudPath!);
            var range = context.Request.Headers["Range"];
            if (!string.IsNullOrWhiteSpace(range)) request.Headers.TryAddWithoutValidation("Range", range);
            using var upstream = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            context.Response.StatusCode = (int)upstream.StatusCode;
            if (!upstream.IsSuccessStatusCode) return;
            context.Response.ContentType = upstream.Content.Headers.ContentType?.ToString() ??
                (Path.GetExtension(track.NextcloudPath!).Equals(".flac", StringComparison.OrdinalIgnoreCase) ? "audio/flac" : "audio/mpeg");
            if (upstream.Content.Headers.ContentLength is long length) context.Response.ContentLength64 = length;
            if (upstream.Content.Headers.ContentRange is not null)
                context.Response.Headers["Content-Range"] = upstream.Content.Headers.ContentRange.ToString();
            context.Response.Headers["Accept-Ranges"] = "bytes";
            if (context.Request.HttpMethod == "HEAD") return;
            await using var stream = await upstream.Content.ReadAsStreamAsync();
            await stream.CopyToAsync(context.Response.OutputStream);
        }
        catch { try { context.Response.StatusCode = 502; } catch { } }
        finally { try { context.Response.Close(); } catch { } }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, bool collection = false)
    {
        var request = new HttpRequestMessage(method, CreateFileUri(path, collection));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_connection.Username}:{_appPassword}")));
        return request;
    }

    private Uri CreateFileUri(string path, bool collection = false)
    {
        var baseUrl = _connection.ServerUrl.TrimEnd('/');
        var user = Uri.EscapeDataString(_connection.Username);
        var encodedPath = string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        return new Uri($"{baseUrl}/remote.php/dav/files/{user}/{encodedPath}{(collection && encodedPath.Length > 0 ? "/" : "")}");
    }

    private void EnsureConnected()
    {
        if (!IsConnected) throw new InvalidOperationException("Nextcloud ist nicht verbunden. Bitte in Einstellungen das App-Passwort eingeben.");
    }

    private static NextcloudConnection Normalize(NextcloudConnection connection)
    {
        if (!Uri.TryCreate(connection.ServerUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Bitte eine gültige HTTPS-Adresse für Nextcloud eingeben.");
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/login", StringComparison.OrdinalIgnoreCase)) path = path[..^6];
        var server = uri.GetLeftPart(UriPartial.Authority) + path;
        var folder = connection.MusicFolder.Trim().Trim('/');
        if (folder.Split('/').Any(part => part is "." or "..")) throw new ArgumentException("Ungültiger Nextcloud-Ordner.");
        return new NextcloudConnection { ServerUrl = server, Username = connection.Username.Trim(), MusicFolder = folder };
    }

    public void Dispose() { _listener?.Close(); _http.Dispose(); }
}

public sealed class NextcloudConnection
{
    public string ServerUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string MusicFolder { get; set; } = "Music";
}
