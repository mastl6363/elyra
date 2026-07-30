using System.Text.Json;
using Elyra.Models;

namespace Elyra.Services;

/// <summary>Persists favorites, listening history and play counts locally.</summary>
public sealed class UserMusicDataService
{
    private const int MaximumHistoryEntries = 100;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ListeningHistoryEntry> _history = [];

    public UserMusicDataService()
        : this(Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "user-music.json")) { }

    public UserMusicDataService(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public IReadOnlyList<ListeningHistoryEntry> History => _history;
    public int FavoriteCount => _favorites.Count;
    public event EventHandler? Changed;

    public bool IsFavorite(Track track) => IsFavorite(track.FilePath);
    public bool IsFavorite(string filePath) => _favorites.Contains(filePath);

    public void ToggleFavorite(Track track)
    {
        if (!_favorites.Add(track.FilePath))
            _favorites.Remove(track.FilePath);
        Persist();
    }

    public IReadOnlyList<Track> ResolveFavorites(IEnumerable<Track> libraryTracks)
    {
        var byPath = libraryTracks.ToDictionary(track => track.FilePath, StringComparer.OrdinalIgnoreCase);
        return _favorites
            .Select(path => byPath.GetValueOrDefault(path))
            .Where(track => track is not null)
            .Cast<Track>()
            .OrderBy(track => track.Artist, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void RecordPlayed(Track track)
    {
        var existing = _history.FirstOrDefault(entry =>
            string.Equals(entry.Track.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase));
        var playCount = (existing?.PlayCount ?? 0) + 1;
        if (existing is not null) _history.Remove(existing);

        _history.Insert(0, new ListeningHistoryEntry
        {
            Track = TrackSnapshot.FromTrack(track),
            LastPlayedAt = DateTimeOffset.UtcNow,
            PlayCount = playCount
        });

        if (_history.Count > MaximumHistoryEntries)
            _history.RemoveRange(MaximumHistoryEntries, _history.Count - MaximumHistoryEntries);
        Persist();
    }

    public IReadOnlyList<Track> RecentlyPlayed(IEnumerable<Track> libraryTracks, int count = 12) =>
        ResolveHistory(libraryTracks, _history.Take(Math.Max(0, count)));

    public IReadOnlyList<Track> MostPlayed(IEnumerable<Track> libraryTracks, int count = 8) =>
        ResolveHistory(
            libraryTracks,
            _history.OrderByDescending(entry => entry.PlayCount)
                .ThenByDescending(entry => entry.LastPlayedAt)
                .Take(Math.Max(0, count)));

    private static IReadOnlyList<Track> ResolveHistory(
        IEnumerable<Track> libraryTracks,
        IEnumerable<ListeningHistoryEntry> entries)
    {
        var byPath = libraryTracks.ToDictionary(track => track.FilePath, StringComparer.OrdinalIgnoreCase);
        return entries.Select(entry =>
                byPath.TryGetValue(entry.Track.FilePath, out var track) ? track : entry.Track.ToTrack())
            .Where(track => File.Exists(track.FilePath))
            .ToList();
    }

    private void Persist()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var state = new UserMusicData
            {
                FavoriteFilePaths = _favorites.Order(StringComparer.OrdinalIgnoreCase).ToList(),
                History = _history
            };
            File.WriteAllText(_filePath, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch { }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var state = JsonSerializer.Deserialize<UserMusicData>(File.ReadAllText(_filePath));
            if (state is null) return;
            _favorites.UnionWith(state.FavoriteFilePaths.Where(path => !string.IsNullOrWhiteSpace(path)));
            _history.AddRange(state.History
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Track.FilePath))
                .OrderByDescending(entry => entry.LastPlayedAt)
                .Take(MaximumHistoryEntries));
        }
        catch { }
    }
}
