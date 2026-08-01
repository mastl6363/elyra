namespace Elyra.Models;

/// <summary>
/// Compact, serializable track representation for queues, history and session restore.
/// Cover art stays in the library cache and is deliberately not duplicated here.
/// </summary>
public sealed class TrackSnapshot
{
    public string FilePath { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string AlbumArtist { get; set; } = "";
    public string Genre { get; set; } = "";
    public uint Year { get; set; }
    public uint TrackNumber { get; set; }
    public uint DiscNumber { get; set; }
    public long DurationMs { get; set; }

    public static TrackSnapshot FromTrack(Track track) => new()
    {
        FilePath = track.FilePath,
        Title = track.Title,
        Artist = track.Artist,
        Album = track.Album,
        AlbumArtist = track.AlbumArtist,
        Genre = track.Genre,
        Year = track.Year,
        TrackNumber = track.TrackNumber,
        DiscNumber = track.DiscNumber,
        DurationMs = (long)track.Duration.TotalMilliseconds
    };

    public Track ToTrack() => new()
    {
        FilePath = FilePath,
        Title = Title,
        Artist = Artist,
        Album = Album,
        AlbumArtist = AlbumArtist,
        Genre = Genre,
        Year = Year,
        TrackNumber = TrackNumber,
        DiscNumber = DiscNumber,
        Duration = TimeSpan.FromMilliseconds(Math.Max(0, DurationMs))
    };
}
