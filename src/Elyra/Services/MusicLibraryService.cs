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
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".flac" };

    public IReadOnlyList<Track> Tracks { get; private set; } = Array.Empty<Track>();
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

            if (normalizedTracks.Where((track, index) => !ReferenceEquals(track, state.Tracks[index])).Any())
            {
                PersistState();
            }
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
            Tracks = await Task.Run(() => ScanFolder(folderPath));
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

    public void Clear()
    {
        Tracks = Array.Empty<Track>();
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
            TrackNumber = track.TrackNumber,
            DiscNumber = track.DiscNumber,
            Duration = track.Duration,
            CoverArtDataUri = track.CoverArtDataUri
        };
    }

    private void PersistState() => _stateStore.Save(new LibraryState
    {
        FolderPath = FolderPath,
        Tracks = Tracks.ToList()
    });
}
