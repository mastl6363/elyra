namespace Elyra.Models;

public sealed class ListeningHistoryEntry
{
    public TrackSnapshot Track { get; set; } = new();
    public DateTimeOffset LastPlayedAt { get; set; }
    public int PlayCount { get; set; }
}

public sealed class UserMusicData
{
    public List<string> FavoriteFilePaths { get; set; } = [];
    public List<ListeningHistoryEntry> History { get; set; } = [];
}
