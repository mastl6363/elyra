using Elyra.Models;

namespace Elyra.Services;

public enum AudioFileFilter
{
    All,
    Mp3,
    Flac,
    Ogg
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

    public static IReadOnlyList<Genre> Apply(
        IEnumerable<Genre> genres,
        string? searchText,
        AudioFileFilter fileFilter)
    {
        var query = searchText?.Trim();

        return genres.Where(genre =>
        {
            var matchingTracks = genre.Tracks.Where(track => MatchesFormat(track, fileFilter));
            if (!matchingTracks.Any())
                return false;

            if (string.IsNullOrEmpty(query))
                return true;

            return Contains(genre.Name, query)
                || matchingTracks.Any(track =>
                    Contains(track.Title, query)
                    || Contains(track.Artist, query)
                    || Contains(track.Album, query));
        }).ToList();
    }

    private static bool Contains(string value, string query) =>
        value.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private static bool MatchesFormat(Track track, AudioFileFilter fileFilter)
    {
        if (fileFilter == AudioFileFilter.All) return true;
        var extension = fileFilter switch
        {
            AudioFileFilter.Mp3 => ".mp3",
            AudioFileFilter.Flac => ".flac",
            AudioFileFilter.Ogg => ".ogg",
            _ => ""
        };
        return string.Equals(Path.GetExtension(track.NextcloudPath ?? track.FilePath), extension,
            StringComparison.OrdinalIgnoreCase);
    }
}
