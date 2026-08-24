namespace Elyra.Models;

/// <summary>
/// A user playlist. Persisted to JSON, so entries store a lightweight snapshot
/// of each track (no cover art — that would bloat the file) and are turned back
/// into <see cref="Track"/> objects for display and playback.
/// </summary>
public sealed class Playlist
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Neue Wiedergabeliste";
    public List<PlaylistEntry> Entries { get; set; } = new();

    public int Count => Entries.Count;

    public IReadOnlyList<Track> Tracks => Entries.Select(e => e.ToTrack()).ToList();
}

/// <summary>A serializable snapshot of a track inside a playlist.</summary>
public sealed class PlaylistEntry
{
    public string FilePath { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string Genre { get; set; } = "";
    public uint Year { get; set; }
    public long DurationMs { get; set; }

    public static PlaylistEntry FromTrack(Track t) => new()
    {
        FilePath = t.FilePath,
        Title = t.Title,
        Artist = t.Artist,
        Album = t.Album,
        Genre = t.Genre,
        Year = t.Year,
        DurationMs = (long)t.Duration.TotalMilliseconds
    };

    public Track ToTrack() => new()
    {
        FilePath = FilePath,
        Title = Title,
        Artist = Artist,
        Album = Album,
        Genre = Genre,
        Year = Year,
        Duration = TimeSpan.FromMilliseconds(DurationMs)
    };
}
