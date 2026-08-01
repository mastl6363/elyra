using Elyra.Models;

namespace Elyra.Services;

public enum AudioFileFilter
{
    All,
    Mp3,
    Flac
}

public static class LibraryFilter
{
    public static IReadOnlyList<Track> Apply(
        IEnumerable<Track> tracks,
        string? searchText,
        AudioFileFilter fileFilter,
        string? artistFilter = null,
        string? genreFilter = null)
    {
        var query = searchText?.Trim();

        return tracks.Where(track =>
        {
            if (!MatchesFormat(track, fileFilter))
                return false;

            if (!string.IsNullOrWhiteSpace(artistFilter)
                && !string.Equals(track.Artist, artistFilter, StringComparison.CurrentCultureIgnoreCase))
                return false;

            if (genreFilter == LibraryBrowseState.MissingGenreFilter)
            {
                if (!string.IsNullOrWhiteSpace(track.Genre))
                    return false;
            }
            else if (!string.IsNullOrWhiteSpace(genreFilter)
                && !string.Equals(track.Genre, genreFilter, StringComparison.CurrentCultureIgnoreCase))
            {
                return false;
            }

            return string.IsNullOrEmpty(query)
                || Contains(track.Title, query)
                || Contains(track.Artist, query)
                || Contains(track.Album, query)
                || Contains(track.Genre, query);
        }).ToList();
    }

    public static IReadOnlyList<Track> Sort(
        IEnumerable<Track> tracks,
        TrackSortOrder sortOrder,
        bool descending)
    {
        Func<Track, object> keySelector = sortOrder switch
        {
            TrackSortOrder.Artist => track => track.Artist,
            TrackSortOrder.Album => track => track.Album,
            TrackSortOrder.Genre => track => track.Genre,
            TrackSortOrder.Duration => track => track.Duration,
            _ => track => track.Title
        };

        var sorted = descending
            ? tracks.OrderByDescending(keySelector, LibrarySortComparer.Instance)
            : tracks.OrderBy(keySelector, LibrarySortComparer.Instance);

        return sorted
            .ThenBy(track => track.Artist, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(track => track.Album, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(track => track.DiscNumber)
            .ThenBy(track => track.TrackNumber)
            .ThenBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<Artist> Apply(
        IEnumerable<Artist> artists,
        string? searchText,
        AudioFileFilter fileFilter)
    {
        var query = searchText?.Trim();

        return artists.Where(artist =>
        {
            var matchingTracks = artist.Tracks.Where(track => MatchesFormat(track, fileFilter));
            if (!matchingTracks.Any())
                return false;

            if (string.IsNullOrEmpty(query))
                return true;

            return Contains(artist.Name, query)
                || matchingTracks.Any(track =>
                    Contains(track.Title, query)
                    || Contains(track.Album, query));
        }).ToList();
    }

    public static IReadOnlyList<Album> Apply(
        IEnumerable<Album> albums,
        string? searchText,
        AudioFileFilter fileFilter)
    {
        var query = searchText?.Trim();

        return albums.Where(album =>
        {
            var matchesFormat = album.Tracks.Any(track => MatchesFormat(track, fileFilter));

            if (!matchesFormat)
                return false;

            if (string.IsNullOrEmpty(query))
                return true;

            return Contains(album.Title, query)
                || Contains(album.Artist, query)
                || album.Tracks.Any(track =>
                    Contains(track.Title, query)
                    || Contains(track.Artist, query)
                    || Contains(track.Album, query));
        }).ToList();
    }

    private static bool Contains(string value, string query) =>
        value.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private static bool MatchesFormat(Track track, AudioFileFilter fileFilter) =>
        fileFilter == AudioFileFilter.All || string.Equals(
            Path.GetExtension(track.FilePath),
            fileFilter == AudioFileFilter.Mp3 ? ".mp3" : ".flac",
            StringComparison.OrdinalIgnoreCase);

    private sealed class LibrarySortComparer : IComparer<object>
    {
        public static LibrarySortComparer Instance { get; } = new();

        public int Compare(object? x, object? y)
        {
            if (x is string left && y is string right)
                return StringComparer.CurrentCultureIgnoreCase.Compare(left, right);

            return Comparer<object>.Default.Compare(x, y);
        }
    }
}
