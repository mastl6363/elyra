namespace Elyra.Models;

/// <summary>A set of tracks grouped under one album, for the Mediathek grid.</summary>
public sealed class Album
{
    /// <summary>Stable id derived from the album key — used for the detail route.</summary>
    public string Id { get; init; } = "";
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public string? CoverArtDataUri { get; init; }
    public required IReadOnlyList<Track> Tracks { get; init; }

    public int TrackCount => Tracks.Count;
}
