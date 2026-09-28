using Elyra.Models;

namespace Elyra.Services;

/// <summary>
/// Builds the two auto-generated playlists shown on the Wiedergabelisten page:
/// the tracks played most often, and other tracks that share genre/artist with
/// those — a lightweight "might fit too" recommendation with no external
/// service or audio analysis involved. Registered as a singleton.
/// </summary>
public sealed class SuggestionService
{
    private readonly MusicLibraryService _library;
    private readonly PlayHistoryService _history;

    public SuggestionService(MusicLibraryService library, PlayHistoryService history)
    {
        _library = library;
        _history = history;
    }

    /// <summary>The tracks played most often, most-played first.</summary>
    public IReadOnlyList<Track> MostPlayed(int take = 25) => _library.Tracks
        .Select(track => (Track: track, Count: _history.GetPlayCount(track.FilePath)))
        .Where(entry => entry.Count > 0)
        .OrderByDescending(entry => entry.Count)
        .ThenByDescending(entry => _history.GetLastPlayed(entry.Track.FilePath))
        .Select(entry => entry.Track)
        .Take(take)
        .ToList();

    /// <summary>
    /// Tracks not already among the most-played, scored by how much their genre
    /// and artist overlap with the most-played tracks' genres/artists. Empty
    /// until there's enough play history to base a suggestion on.
    /// </summary>
    public IReadOnlyList<Track> Recommended(int take = 25)
    {
        var mostPlayed = MostPlayed(50);
        if (mostPlayed.Count == 0)
            return [];

        var mostPlayedPaths = mostPlayed
            .Select(track => track.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var genreWeights = mostPlayed
            .Where(track => !string.IsNullOrWhiteSpace(track.Genre))
            .GroupBy(track => track.Genre.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.CurrentCultureIgnoreCase);

        var artistWeights = mostPlayed
            .GroupBy(track => track.Artist.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.CurrentCultureIgnoreCase);

        return _library.Tracks
            .Where(track => !mostPlayedPaths.Contains(track.FilePath))
            .Select(track => (Track: track, Score: Score(track, genreWeights, artistWeights)))
            .Where(entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Track.Artist, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.Track.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(entry => entry.Track)
            .Take(take)
            .ToList();
    }

    private static int Score(
        Track track,
        IReadOnlyDictionary<string, int> genreWeights,
        IReadOnlyDictionary<string, int> artistWeights)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(track.Genre) && genreWeights.TryGetValue(track.Genre.Trim(), out var genreWeight))
            score += genreWeight * 2;
        if (artistWeights.TryGetValue(track.Artist.Trim(), out var artistWeight))
            score += artistWeight;
        return score;
    }
}
