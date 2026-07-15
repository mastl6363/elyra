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
}
