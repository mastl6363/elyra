using System.Text.Json;
using Elyra.Models;

namespace Elyra.Services;

/// <summary>
/// Manages user playlists and persists them as JSON in the app-data folder.
/// Phase 2 will move this into the SQLite cache. Registered as a singleton.
/// </summary>
public sealed class PlaylistService
{
    private readonly string _filePath;
    private readonly List<Playlist> _playlists = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public PlaylistService()
        : this(Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "playlists.json"))
    {
    }

    public PlaylistService(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public IReadOnlyList<Playlist> Playlists => _playlists;

    /// <summary>Raised whenever a playlist is created, changed or removed.</summary>
    public event EventHandler? Changed;

    public Playlist? Find(string id) => _playlists.FirstOrDefault(p => p.Id == id);

    public Playlist Create(string? name = null)
    {
        var playlist = new Playlist
        {
            Name = string.IsNullOrWhiteSpace(name) ? NextDefaultName() : name.Trim()
        };
        _playlists.Add(playlist);
        Persist();
        return playlist;
    }

    public void Rename(string id, string name)
    {
        var playlist = Find(id);
        if (playlist is null || string.IsNullOrWhiteSpace(name)) return;
        playlist.Name = name.Trim();
        Persist();
    }

    public void Delete(string id)
    {
        if (_playlists.RemoveAll(p => p.Id == id) > 0)
            Persist();
    }

    public void AddTrack(string playlistId, Track track)
    {
        var playlist = Find(playlistId);
        if (playlist is null) return;
        // Windows file paths are case-insensitive; compare the same way MusicLibraryService does
        // so a re-scanned track with different path casing isn't treated as a different file.
        if (playlist.Entries.Any(e => string.Equals(e.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase)))
            return; // skip duplicates
        playlist.Entries.Add(PlaylistEntry.FromTrack(track));
        Persist();
    }

    public void RemoveTrack(string playlistId, string filePath)
    {
        var playlist = Find(playlistId);
        if (playlist is null) return;
        if (playlist.Entries.RemoveAll(e => string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase)) > 0)
            Persist();
    }

    private string NextDefaultName()
    {
        var index = _playlists.Count + 1;
        string name;
        do { name = $"Neue Wiedergabeliste {index++}"; }
        while (_playlists.Any(p => p.Name == name));
        return name;
    }

    private void Persist()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            // Write-then-rename instead of overwriting in place, so a crash mid-write
            // can never leave a truncated/corrupt playlists file behind.
            var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_playlists, JsonOptions));
            File.Move(temporaryPath, _filePath, true);
        }
        catch { /* best-effort persistence */ }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var loaded = JsonSerializer.Deserialize<List<Playlist>>(File.ReadAllText(_filePath));
            if (loaded is not null)
            {
                _playlists.Clear();
                _playlists.AddRange(loaded);
            }
        }
        catch { /* ignore a corrupt/old file */ }
    }
}
