using Elyra.Models;

namespace Elyra.Services;

/// <summary>
/// Scans a folder for local audio files and reads their tags via TagLib#.
/// Phase 1 keeps the whole library in memory; Phase 2 will cache it in SQLite.
/// Registered as a singleton.
/// </summary>
public sealed class MusicLibraryService
{
    private readonly ILibraryStateStore _stateStore;
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".flac", ".ogg" };

    public IReadOnlyList<Track> Tracks { get; private set; } = Array.Empty<Track>();
    private IReadOnlyList<Album> _albums = Array.Empty<Album>();
    private IReadOnlyList<Artist> _artists = Array.Empty<Artist>();
    private IReadOnlyList<Genre> _genres = Array.Empty<Genre>();
    public string? FolderPath { get; private set; }
    public bool IsScanning { get; private set; }

    public MusicLibraryService(ILibraryStateStore stateStore)
    {
        _stateStore = stateStore;
        var state = _stateStore.Load();
        if (state is not null)
        {
            FolderPath = state.FolderPath;
            var normalizedTracks = state.Tracks.Select(NormalizeLegacyTrack).ToList();
            Tracks = normalizedTracks;
            RebuildViews();

            if (normalizedTracks.Where((track, index) => !ReferenceEquals(track, state.Tracks[index])).Any())
            {
                PersistState();
            }
        }
    }

    /// <summary>Raised when the library starts/finishes scanning or its contents change.</summary>
    public event EventHandler? Changed;

    /// <summary>Tracks with album metadata, grouped into albums.</summary>
    public IReadOnlyList<Album> Albums => _albums;

    /// <summary>All tracks grouped by their credited artist.</summary>
    public IReadOnlyList<Artist> Artists => _artists;

    /// <summary>All tracks with a genre tag, grouped by genre.</summary>
    public IReadOnlyList<Genre> Genres => _genres;

    private void RebuildViews()
    {
        _albums = BuildAlbums(Tracks);
        _artists = Tracks
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

        _genres = Tracks
        .Where(t => !string.IsNullOrWhiteSpace(t.Genre))
        .GroupBy(t => t.Genre.Trim(), StringComparer.CurrentCultureIgnoreCase)
        .Select(group =>
        {
            var genreTracks = group
                .OrderBy(t => t.Artist, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(t => t.Album, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(t => t.DiscNumber)
                .ThenBy(t => t.TrackNumber)
                .ToList();

            return new Genre
            {
                Id = StableId($"genre\0{group.Key}"),
                Name = group.Key,
                CoverArtDataUri = genreTracks.FirstOrDefault(t => t.CoverArtDataUri is not null)?.CoverArtDataUri,
                Tracks = genreTracks,
                Albums = BuildAlbums(genreTracks)
            };
        })
        .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();
    }

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

    public Genre? FindGenre(string id) => Genres.FirstOrDefault(g => g.Id == id);

    private static string StableId(string value) =>
        Convert.ToHexString(System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(value)));

    /// <summary>Scans <paramref name="folderPath"/> recursively and replaces the library.</summary>
    public async Task ImportFolderAsync(string folderPath)
    {
        IsScanning = true;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var localTracks = await Task.Run(() => ScanFolder(folderPath));
            Tracks = localTracks.Concat(Tracks.Where(t => t.NextcloudPath is not null)).ToList();
            RebuildViews();
            FolderPath = folderPath;
            PersistState();
        }
        finally
        {
            IsScanning = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task RefreshAsync() => string.IsNullOrWhiteSpace(FolderPath)
        ? Task.CompletedTask
        : ImportFolderAsync(FolderPath);

    public void SetNextcloudTracks(IEnumerable<Track> remoteTracks)
    {
        Tracks = Tracks.Where(t => t.NextcloudPath is null).Concat(remoteTracks).ToList();
        RebuildViews();
        PersistState();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveNextcloudTracks() => SetNextcloudTracks([]);

    public void RemoveLocalTracks()
    {
        Tracks = Tracks.Where(t => t.NextcloudPath is not null).ToList();
        FolderPath = null;
        RebuildViews();
        PersistState();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyCachedNextcloudTrack(Track track, string cachedFile)
    {
        Track enriched;
        try { enriched = ReadCachedNextcloudTrack(track, cachedFile); }
        catch { return; }
        var current = Tracks.FirstOrDefault(t => t.FilePath == track.FilePath);
        if (current is null || current.NextcloudEtag != track.NextcloudEtag) return;
        Tracks = Tracks.Select(t => t.FilePath == track.FilePath ? enriched : t).ToList();
        RebuildViews();
        PersistState();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static Track ReadCachedNextcloudTrack(Track track, string cachedFile)
    {
        using var file = TagLib.File.Create(cachedFile);
        var tag = file.Tag;
        var picture = tag.Pictures.FirstOrDefault(p => p.Data?.Data?.Length > 0);
        var cover = picture is null ? track.CoverArtDataUri :
            $"data:{(string.IsNullOrWhiteSpace(picture.MimeType) ? "image/jpeg" : picture.MimeType)};base64,{Convert.ToBase64String(picture.Data.Data)}";
        return new Track
        {
            FilePath = track.FilePath,
            NextcloudPath = track.NextcloudPath,
            NextcloudEtag = track.NextcloudEtag,
            Title = string.IsNullOrWhiteSpace(tag.Title) ? track.Title : tag.Title,
            Artist = string.IsNullOrWhiteSpace(tag.FirstPerformer) ? track.Artist : tag.FirstPerformer,
            Album = string.IsNullOrWhiteSpace(tag.Album) ? track.Album : tag.Album,
            AlbumArtist = string.IsNullOrWhiteSpace(tag.FirstAlbumArtist) ? track.AlbumArtist : tag.FirstAlbumArtist,
            Genre = string.IsNullOrWhiteSpace(tag.FirstGenre) ? track.Genre : tag.FirstGenre,
            TrackNumber = tag.Track == 0 ? track.TrackNumber : tag.Track,
            DiscNumber = tag.Disc == 0 ? track.DiscNumber : tag.Disc,
            Duration = file.Properties?.Duration ?? track.Duration,
            CoverArtDataUri = cover
        };
    }

    public void Clear()
    {
        Tracks = Array.Empty<Track>();
        RebuildViews();
        FolderPath = null;
        _stateStore.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
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
            RebuildViews();
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
            TrackNumber = track.TrackNumber,
            DiscNumber = track.DiscNumber,
            Duration = track.Duration,
            CoverArtDataUri = track.CoverArtDataUri,
            NextcloudPath = track.NextcloudPath,
            NextcloudEtag = track.NextcloudEtag
        };
    }

    private void PersistState() => _stateStore.Save(new LibraryState
    {
        FolderPath = FolderPath,
        Tracks = Tracks.ToList()
    });
}
