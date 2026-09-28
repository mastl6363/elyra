namespace Elyra.Models;

/// <summary>A genre with all locally available songs and their albums.</summary>
public sealed class Genre
{
    public string Id { get; init; } = "";
    public required string Name { get; init; }
    public string? CoverArtDataUri { get; init; }
    public required IReadOnlyList<Track> Tracks { get; init; }
    public required IReadOnlyList<Album> Albums { get; init; }

    public int TrackCount => Tracks.Count;
    public int AlbumCount => Albums.Count;
}
