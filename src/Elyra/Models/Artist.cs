namespace Elyra.Models;

/// <summary>An artist with all locally available songs and optional albums.</summary>
public sealed class Artist
{
    public string Id { get; init; } = "";
    public required string Name { get; init; }
    public string? CoverArtDataUri { get; init; }
    public required IReadOnlyList<Track> Tracks { get; init; }
    public required IReadOnlyList<Album> Albums { get; init; }

    public int TrackCount => Tracks.Count;
    public int AlbumCount => Albums.Count;
}
