namespace Elyra.Models;

public sealed record LyricsLine(TimeSpan? Timestamp, string Text);

public sealed record LyricsDocument(
    string Source,
    IReadOnlyList<LyricsLine> Lines)
{
    public bool IsSynchronized => Lines.Any(line => line.Timestamp is not null);
}
