using Elyra.Models;

namespace Elyra.Services;

/// <summary>
/// Scans a folder for local audio files and reads their tags via TagLib#.
/// Phase 1 keeps the whole library in memory; Phase 2 will cache it in SQLite.
/// Registered as a singleton.
/// </summary>
public sealed class MusicLibraryService
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".flac" };

    public IReadOnlyList<Track> Tracks { get; private set; } = Array.Empty<Track>();
    public bool IsScanning { get; private set; }

    /// <summary>Raised when the library starts/finishes scanning or its contents change.</summary>
    public event EventHandler? Changed;

    /// <summary>Tracks grouped into albums, sorted by artist then album title.</summary>
    public IReadOnlyList<Album> Albums => Tracks
        .GroupBy(t => t.AlbumKey)
        .Select(g =>
        {
            var first = g.First();
            return new Album
            {
                Id = AlbumId(g.Key),
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

    private static string AlbumId(string albumKey) =>
        Convert.ToHexString(System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(albumKey)));

    /// <summary>Scans <paramref name="folderPath"/> recursively and replaces the library.</summary>
    public async Task ImportFolderAsync(string folderPath)
    {
        IsScanning = true;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            Tracks = await Task.Run(() => ScanFolder(folderPath));
        }
        finally
        {
            IsScanning = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static List<Track> ScanFolder(string folderPath)
    {
        var result = new List<Track>();

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)));
        }
        catch
        {
            return result; // folder vanished or access denied
        }

        foreach (var file in files)
        {
            try { result.Add(ReadTrack(file)); }
            catch { /* skip files TagLib# can't parse */ }
        }
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
            Album = string.IsNullOrWhiteSpace(tag.Album) ? "Unbekanntes Album" : tag.Album,
            AlbumArtist = tag.FirstAlbumArtist ?? string.Empty,
            TrackNumber = tag.Track,
            DiscNumber = tag.Disc,
            Duration = tfile.Properties?.Duration ?? TimeSpan.Zero,
            CoverArtDataUri = coverUri
        };
    }
}
