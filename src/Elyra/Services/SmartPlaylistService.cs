using Elyra.Models;

namespace Elyra.Services;

public sealed class SmartPlaylistService(
    MusicLibraryService library,
    UserMusicDataService userMusic)
{
    public IReadOnlyList<SmartPlaylistDefinition> GetAll()
    {
        var tracks = library.Tracks;
        var playedPaths = userMusic.History
            .Select(entry => entry.Track.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var definitions = new List<SmartPlaylistDefinition>
        {
            new(
                "unplayed",
                "Noch ungehört",
                "Titel, die in Elyra noch nie abgespielt wurden",
                tracks.Where(track => !playedPaths.Contains(track.FilePath))
                    .OrderBy(track => track.Artist, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
                "Neu"),
            new(
                "rediscover",
                "Wiederentdecken",
                "Lange nicht gehörte Titel aus deiner Sammlung",
                Rediscover(tracks, userMusic.History),
                "Mix"),
            new(
                "recent-files",
                "Kürzlich hinzugefügt",
                "Die zuletzt geänderten oder hinzugefügten Musikdateien",
                tracks.OrderByDescending(FileTimestamp)
                    .Take(100)
                    .ToList(),
                "Neu"),
            new(
                "lossless",
                "Lossless",
                "Alle lokal verfügbaren FLAC-Titel",
                tracks.Where(track => string.Equals(Path.GetExtension(track.FilePath), ".flac", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(track => track.Artist, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(track => track.Album, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(track => track.TrackNumber)
                    .ToList(),
                "FLAC")
        };

        definitions.AddRange(tracks
            .Where(track => !string.IsNullOrWhiteSpace(track.Genre))
            .GroupBy(track => track.Genre.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Where(group => group.Count() >= 5)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Take(4)
            .Select(group => new SmartPlaylistDefinition(
                $"genre-{Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(group.Key)))[..10]}",
                $"{group.Key} Mix",
                $"Alle Titel aus dem Genre {group.Key}",
                group.OrderBy(track => track.Artist, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
                "Genre")));

        return definitions;
    }

    public SmartPlaylistDefinition? Find(string id) =>
        GetAll().FirstOrDefault(playlist => string.Equals(playlist.Id, id, StringComparison.Ordinal));

    private static IReadOnlyList<Track> Rediscover(
        IReadOnlyList<Track> tracks,
        IReadOnlyList<ListeningHistoryEntry> history)
    {
        var lastPlayed = history.ToDictionary(
            entry => entry.Track.FilePath,
            entry => entry.LastPlayedAt,
            StringComparer.OrdinalIgnoreCase);

        return tracks
            .Where(track => lastPlayed.ContainsKey(track.FilePath))
            .OrderBy(track => lastPlayed[track.FilePath])
            .Take(50)
            .ToList();
    }

    private static DateTime FileTimestamp(Track track)
    {
        try { return File.GetLastWriteTimeUtc(track.FilePath); }
        catch { return DateTime.MinValue; }
    }
}
