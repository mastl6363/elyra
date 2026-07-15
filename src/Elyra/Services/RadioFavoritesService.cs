using System.Text.Json;
using Elyra.Models;

namespace Elyra.Services;

/// <summary>Persists the user's favorite radio stations locally.</summary>
public sealed class RadioFavoritesService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly List<RadioStation> _favorites = [];

    public RadioFavoritesService()
        : this(Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "radio-favorites.json")) { }

    public RadioFavoritesService(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public IReadOnlyList<RadioStation> Favorites => _favorites;
    public event EventHandler? Changed;

    public bool Contains(string stationUuid) =>
        _favorites.Any(station => string.Equals(
            station.StationUuid,
            stationUuid,
            StringComparison.OrdinalIgnoreCase));

    public void Toggle(RadioStation station)
    {
        var existing = _favorites.FindIndex(item => string.Equals(
            item.StationUuid,
            station.StationUuid,
            StringComparison.OrdinalIgnoreCase));

        if (existing >= 0)
            _favorites.RemoveAt(existing);
        else
            _favorites.Add(station);

        Persist();
    }

    private void Persist()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_favorites, JsonOptions));
        }
        catch
        {
            // Favorites are best-effort; a read-only app-data folder must not crash playback.
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var loaded = JsonSerializer.Deserialize<List<RadioStation>>(File.ReadAllText(_filePath));
            if (loaded is not null)
                _favorites.AddRange(loaded.Where(station => RadioStation.IsHttpUrl(station.StreamUrl)));
        }
        catch
        {
            // Ignore a corrupt or outdated favorites file.
        }
    }
}
