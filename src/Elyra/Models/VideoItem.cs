namespace Elyra.Models;

public enum VideoSourceKind
{
    File,
    Dvd
}

/// <summary>A local movie or an optical DVD drive.</summary>
public sealed class VideoItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Source { get; set; } = "";
    public VideoSourceKind Kind { get; set; }
    public long PositionMs { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastPlayedAt { get; set; }
    public bool IsAvailable { get; set; } = true;

    public TimeSpan SavedPosition => TimeSpan.FromMilliseconds(Math.Max(0, PositionMs));
    public string FileName => Kind == VideoSourceKind.Dvd ? Source : Path.GetFileName(Source);
    public string PlaybackLocation => Kind == VideoSourceKind.Dvd ? BuildDvdLocation(Source) : Source;

    public static string BuildDvdLocation(string driveRoot)
    {
        var root = driveRoot.Trim().Replace('\\', '/').TrimEnd('/');
        return $"dvd:///{root}/";
    }
}
