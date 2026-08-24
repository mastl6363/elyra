namespace Elyra.Models;

public sealed class TrackMetadataChanges
{
    public bool ChangeTitle { get; init; }
    public string Title { get; init; } = "";
    public bool ChangeArtist { get; init; }
    public string Artist { get; init; } = "";
    public bool ChangeAlbum { get; init; }
    public string Album { get; init; } = "";
    public bool ChangeGenre { get; init; }
    public string Genre { get; init; } = "";
    public bool ChangeYear { get; init; }
    public uint Year { get; init; }
}

public sealed record TrackMetadataEditResult(int Updated, IReadOnlyList<string> FailedFiles);
