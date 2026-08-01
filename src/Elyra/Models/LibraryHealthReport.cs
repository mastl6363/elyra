namespace Elyra.Models;

public sealed record DuplicateTrackGroup(string Label, IReadOnlyList<Track> Tracks);

public sealed record LibraryHealthReport(
    IReadOnlyList<Track> MissingFiles,
    IReadOnlyList<DuplicateTrackGroup> PossibleDuplicates)
{
    public static LibraryHealthReport Empty { get; } = new([], []);
}
