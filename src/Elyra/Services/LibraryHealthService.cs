using Elyra.Models;

namespace Elyra.Services;

public sealed class LibraryHealthService
{
    public Task<LibraryHealthReport> AnalyzeAsync(
        IEnumerable<Track> tracks,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var snapshot = tracks.ToList();
        var missing = snapshot.Where(track => !File.Exists(track.FilePath)).ToList();
        var duplicates = snapshot
            .Where(track => File.Exists(track.FilePath))
            .GroupBy(DuplicateKey, StringComparer.CurrentCultureIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new DuplicateTrackGroup(
                $"{group.First().Artist} – {group.First().Title}",
                group.OrderBy(track => track.FilePath, StringComparer.OrdinalIgnoreCase).ToList()))
            .OrderBy(group => group.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        cancellationToken.ThrowIfCancellationRequested();
        return new LibraryHealthReport(missing, duplicates);
    }, cancellationToken);

    private static string DuplicateKey(Track track)
    {
        var durationBucket = (long)Math.Round(track.Duration.TotalSeconds);
        return $"{track.Artist.Trim()}\0{track.Title.Trim()}\0{durationBucket}";
    }
}
