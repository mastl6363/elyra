namespace Elyra.Models;

/// <summary>
/// A single playable audio file plus the metadata read from its tags.
/// Immutable — the library rebuilds these on each scan (Phase 1 keeps the whole
/// library in memory; SQLite-backed caching arrives in Phase 2).
/// </summary>
public sealed class Track
{
    public required string FilePath { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public required string Album { get; init; }
    public string AlbumArtist { get; init; } = "";
    public string Genre { get; init; } = "";
    public uint TrackNumber { get; init; }
    public uint DiscNumber { get; init; }
    public TimeSpan Duration { get; init; }

    /// <summary>Embedded cover art as a ready-to-use data URI, or null if none.</summary>
    public string? CoverArtDataUri { get; init; }

    /// <summary>
    /// Grouping key for the album grid: album artist (fallback artist) + album,
    /// joined with U+001F (a control character that never appears in real tag
    /// text) so artist "Boy" + album "George Live" cannot collide with
    /// artist "Boy George" + album "Live".
    /// </summary>
    public string AlbumKey => $"{(string.IsNullOrWhiteSpace(AlbumArtist) ? Artist : AlbumArtist)}{Album}";

    public string DurationLabel => Duration.TotalHours >= 1
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"m\:ss");
}
