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
    {
        _filePath = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "playlists.json");
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
        if (playlist.Entries.Any(e => e.FilePath == track.FilePath)) return; // skip duplicates
        playlist.Entries.Add(PlaylistEntry.FromTrack(track));
        Persist();
    }

    public void RemoveTrack(string playlistId, string filePath)
    {
        var playlist = Find(playlistId);
        if (playlist is null) return;
        if (playlist.Entries.RemoveAll(e => e.FilePath == filePath) > 0)
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
        try { File.WriteAllText(_filePath, JsonSerializer.Serialize(_playlists, JsonOptions)); }
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
