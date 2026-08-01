using Elyra.Models;

namespace Elyra.Services;

/// <summary>
/// Scans a folder for local audio files and reads their tags via TagLib#.
/// The active library stays in memory while SQLite provides the durable local index.
/// Registered as a singleton.
/// </summary>
public sealed class MusicLibraryService : IDisposable
{
    private const int CurrentMetadataVersion = 3;
    private readonly ILibraryStateStore _stateStore;
    private readonly SemaphoreSlim _libraryGate = new(1, 1);
    private readonly object _watcherSync = new();
    private FileSystemWatcher? _watcher;
    private Timer? _watcherTimer;
    private bool _disposed;
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".flac" };

    public IReadOnlyList<Track> Tracks { get; private set; } = Array.Empty<Track>();
    public string? FolderPath { get; private set; }
    public bool IsScanning { get; private set; }
    public bool IsWatching => _watcher?.EnableRaisingEvents == true;

    public MusicLibraryService(ILibraryStateStore stateStore)
    {
        _stateStore = stateStore;
        var state = _stateStore.Load();
        if (state is not null)
        {
            var requiresMetadataRefresh = state.MetadataVersion < CurrentMetadataVersion;
            FolderPath = state.FolderPath;
            var normalizedTracks = state.Tracks.Select(NormalizeLegacyTrack).ToList();
            Tracks = normalizedTracks;

            if (normalizedTracks.Where((track, index) => !ReferenceEquals(track, state.Tracks[index])).Any())
            {
                PersistState();
            }

            if (requiresMetadataRefresh
                && !string.IsNullOrWhiteSpace(FolderPath)
                && Directory.Exists(FolderPath))
            {
                _ = RefreshLegacyMetadataAsync(FolderPath);
            }

            ConfigureWatcher();
        }
    }

    /// <summary>Raised when the library starts/finishes scanning or its contents change.</summary>
    public event EventHandler? Changed;

    /// <summary>Tracks with album metadata, grouped into albums.</summary>
    public IReadOnlyList<Album> Albums => BuildAlbums(Tracks);

    /// <summary>All tracks grouped by their credited artist.</summary>
    public IReadOnlyList<Artist> Artists => Tracks
        .GroupBy(t => t.Artist.Trim(), StringComparer.CurrentCultureIgnoreCase)
        .Select(group =>
        {
            var artistTracks = group
                .OrderBy(t => t.Album, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(t => t.DiscNumber)
                .ThenBy(t => t.TrackNumber)
                .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            return new Artist
            {
                Id = StableId($"artist\0{group.Key}"),
                Name = group.Key,
                CoverArtDataUri = artistTracks.FirstOrDefault(t => t.CoverArtDataUri is not null)?.CoverArtDataUri,
                Tracks = artistTracks,
                Albums = BuildAlbums(artistTracks)
            };
        })
        .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    private static IReadOnlyList<Album> BuildAlbums(IEnumerable<Track> tracks) => tracks
        .Where(t => !string.IsNullOrWhiteSpace(t.Album))
        .GroupBy(t => t.AlbumKey)
        .Select(g =>
        {
            var first = g.First();
            return new Album
            {
                Id = StableId($"album\0{g.Key}"),
                Title = first.Album,
                Artist = string.IsNullOrWhiteSpace(first.AlbumArtist) ? first.Artist : first.AlbumArtist,
                CoverArtDataUri = g.FirstOrDefault(t => t.CoverArtDataUri is not null)?.CoverArtDataUri,
                Tracks = g.OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber).ToList()
            };
        })
        .OrderBy(a => a.Artist, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    /// <summary>Looks up a single album by its stable id (for the detail page).</summary>
    public Album? FindAlbum(string id) => Albums.FirstOrDefault(a => a.Id == id);

    public Artist? FindArtist(string id) => Artists.FirstOrDefault(a => a.Id == id);

    public string GetArtistId(string artistName) =>
        StableId($"artist\0{artistName.Trim()}");

    public string? GetAlbumId(Track track) =>
        string.IsNullOrWhiteSpace(track.Album)
            ? null
            : StableId($"album\0{track.AlbumKey}");

    private static string StableId(string value) =>
        Convert.ToHexString(System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(value)));

    /// <summary>Scans <paramref name="folderPath"/> recursively and replaces the library.</summary>
    public async Task ImportFolderAsync(string folderPath)
    {
        await _libraryGate.WaitAsync();
        try
        {
            IsScanning = true;
            Changed?.Invoke(this, EventArgs.Empty);
            try
            {
                Tracks = await Task.Run(() => ScanFolder(folderPath));
                FolderPath = folderPath;
                PersistState();
                ConfigureWatcher();
            }
            finally
            {
                IsScanning = false;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _libraryGate.Release();
        }
    }

    public Task RefreshAsync() => string.IsNullOrWhiteSpace(FolderPath)
        ? Task.CompletedTask
        : ImportFolderAsync(FolderPath);

    private async Task RefreshLegacyMetadataAsync(string folderPath)
    {
        try
        {
            await ImportFolderAsync(folderPath);
        }
        catch
        {
            // Keep the restored snapshot when a previously available folder
            // disappears or becomes inaccessible during the one-time upgrade.
        }
    }

    public void Clear()
    {
        DisposeWatcher();
        Tracks = Array.Empty<Track>();
        FolderPath = null;
        _stateStore.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveMissingFiles()
    {
        var existing = Tracks.Where(track => File.Exists(track.FilePath)).ToList();
        if (existing.Count == Tracks.Count)
            return;

        Tracks = existing;
        PersistState();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<TrackMetadataEditResult> UpdateTrackMetadataAsync(
        IEnumerable<string> filePaths,
        TrackMetadataChanges changes)
    {
        var selectedPaths = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedPaths.Count == 0)
            return new TrackMetadataEditResult(0, []);

        await _libraryGate.WaitAsync();
        try
        {
            var result = await Task.Run(() =>
            {
                var updated = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
                var failed = new List<string>();

                foreach (var path in selectedPaths)
                {
                    try
                    {
                        using (var file = TagLib.File.Create(path))
                        {
                            if (changes.ChangeTitle) file.Tag.Title = changes.Title.Trim();
                            if (changes.ChangeArtist) file.Tag.Performers = [changes.Artist.Trim()];
                            if (changes.ChangeAlbum) file.Tag.Album = changes.Album.Trim();
                            if (changes.ChangeGenre)
                            {
                                file.Tag.Genres = string.IsNullOrWhiteSpace(changes.Genre)
                                    ? []
                                    : [changes.Genre.Trim()];
                            }
                            if (changes.ChangeYear) file.Tag.Year = changes.Year;
                            file.Save();
                        }

                        updated[path] = ReadTrack(path);
                    }
                    catch
                    {
                        failed.Add(path);
                    }
                }

                return (Updated: updated, Failed: failed);
            });

            if (result.Updated.Count > 0)
            {
                Tracks = Tracks.Select(track =>
                    result.Updated.TryGetValue(track.FilePath, out var updated) ? updated : track).ToList();
                PersistState();
                Changed?.Invoke(this, EventArgs.Empty);
            }

            return new TrackMetadataEditResult(result.Updated.Count, result.Failed);
        }
        finally
        {
            _libraryGate.Release();
        }
    }

    public int ApplyMetadataUpdates(IEnumerable<TrackMetadataUpdate> updates)
    {
        var byFilePath = updates
            .Where(update => !string.IsNullOrWhiteSpace(update.Album))
            .GroupBy(update => update.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var updatedCount = 0;
        Tracks = Tracks.Select(track =>
        {
            if (!string.IsNullOrWhiteSpace(track.Album)
                || !byFilePath.TryGetValue(track.FilePath, out var update))
                return track;

            updatedCount++;
            return CopyTrack(track, update.Album, update.AlbumArtist);
        }).ToList();

        if (updatedCount > 0)
        {
            PersistState();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return updatedCount;
    }

    private static List<Track> ScanFolder(string folderPath)
    {
        var result = new List<Track>();

        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Der Musikordner wurde nicht gefunden: {folderPath}");

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folderPath, "*.*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true
                })
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)));
        }
        catch
        {
            return result; // folder vanished or access denied
        }

        try
        {
            foreach (var file in files)
            {
                try { result.Add(ReadTrack(file)); }
                catch { /* skip files TagLib# can't parse */ }
            }
        }
        catch { /* keep files already read if enumeration fails part-way through */ }

        return result;
    }

    private static Track ReadTrack(string path)
    {
        using var tfile = TagLib.File.Create(path);
        var tag = tfile.Tag;

        string? coverUri = null;
        var picture = tag.Pictures.FirstOrDefault(p => p.Data?.Data?.Length > 0);
        if (picture is not null)
        {
            var mime = string.IsNullOrEmpty(picture.MimeType) ? "image/jpeg" : picture.MimeType;
            coverUri = $"data:{mime};base64,{Convert.ToBase64String(picture.Data.Data)}";
        }

        return new Track
        {
            FilePath = path,
            Title = string.IsNullOrWhiteSpace(tag.Title) ? Path.GetFileNameWithoutExtension(path) : tag.Title,
            Artist = string.IsNullOrWhiteSpace(tag.FirstPerformer) ? "Unbekannter Künstler" : tag.FirstPerformer,
            Album = tag.Album?.Trim() ?? string.Empty,
            AlbumArtist = tag.FirstAlbumArtist ?? string.Empty,
            Genre = tag.FirstGenre?.Trim() ?? string.Empty,
            Year = tag.Year,
            TrackNumber = tag.Track,
            DiscNumber = tag.Disc,
            Duration = tfile.Properties?.Duration ?? TimeSpan.Zero,
            CoverArtDataUri = coverUri
        };
    }

    private static Track NormalizeLegacyTrack(Track track)
    {
        if (!string.Equals(track.Album, "Unbekanntes Album", StringComparison.OrdinalIgnoreCase))
            return track;

        return CopyTrack(track, string.Empty, track.AlbumArtist);
    }

    private static Track CopyTrack(Track track, string album, string albumArtist)
    {
        return new Track
        {
            FilePath = track.FilePath,
            Title = track.Title,
            Artist = track.Artist,
            Album = album,
            AlbumArtist = albumArtist,
            Genre = track.Genre,
            Year = track.Year,
            TrackNumber = track.TrackNumber,
            DiscNumber = track.DiscNumber,
            Duration = track.Duration,
            CoverArtDataUri = track.CoverArtDataUri
        };
    }

    private void PersistState() => _stateStore.Save(new LibraryState
    {
        MetadataVersion = CurrentMetadataVersion,
        FolderPath = FolderPath,
        Tracks = Tracks.ToList()
    });

    private void ConfigureWatcher()
    {
        DisposeWatcher();
        if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
            return;

        try
        {
            _watcher = new FileSystemWatcher(FolderPath)
            {
                IncludeSubdirectories = true,
                Filter = "*.*",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _watcher.Created += OnWatchedFileChanged;
            _watcher.Changed += OnWatchedFileChanged;
            _watcher.Deleted += OnWatchedFileChanged;
            _watcher.Renamed += OnWatchedFileRenamed;
            _watcher.Error += OnWatcherError;
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            DisposeWatcher();
        }
    }

    private void OnWatchedFileChanged(object sender, FileSystemEventArgs args)
    {
        if (SupportedExtensions.Contains(Path.GetExtension(args.FullPath)))
            ScheduleWatchedRefresh();
    }

    private void OnWatchedFileRenamed(object sender, RenamedEventArgs args)
    {
        if (SupportedExtensions.Contains(Path.GetExtension(args.FullPath))
            || SupportedExtensions.Contains(Path.GetExtension(args.OldFullPath)))
            ScheduleWatchedRefresh();
    }

    private void OnWatcherError(object sender, ErrorEventArgs args) => ScheduleWatchedRefresh();

    private void ScheduleWatchedRefresh()
    {
        lock (_watcherSync)
        {
            _watcherTimer?.Dispose();
            _watcherTimer = new Timer(
                _ => _ = RefreshFromWatcherAsync(),
                null,
                TimeSpan.FromMilliseconds(1200),
                Timeout.InfiniteTimeSpan);
        }
    }

    private async Task RefreshFromWatcherAsync()
    {
        try
        {
            var folder = FolderPath;
            if (!_disposed && !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                await ImportFolderAsync(folder);
        }
        catch
        {
            // A removable source may disappear between the watcher event and scan.
        }
    }

    private void DisposeWatcher()
    {
        lock (_watcherSync)
        {
            _watcherTimer?.Dispose();
            _watcherTimer = null;
            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        DisposeWatcher();
        _libraryGate.Dispose();
    }
}
