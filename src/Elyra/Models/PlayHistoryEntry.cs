namespace Elyra.Models;

/// <summary>How often and when a track (by file path) was last played.</summary>
public sealed class PlayHistoryEntry
{
    public required string FilePath { get; init; }
    public int PlayCount { get; set; }
    public DateTimeOffset LastPlayedUtc { get; set; }
}
