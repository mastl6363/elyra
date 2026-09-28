using System.Text.Json;
using Elyra.Models;

namespace Elyra.Services;

/// <summary>
/// Tracks how often local tracks are played, keyed by file path. Used by
/// <see cref="SuggestionService"/> to build the "Meistgehört" / "Empfehlungen"
/// auto-playlists. Registered as a singleton.
/// </summary>
public sealed class PlayHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly Dictionary<string, PlayHistoryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public PlayHistoryService()
        : this(Path.Combine(AppPaths.DataDirectory, "play-history.json")) { }

    public PlayHistoryService(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public event EventHandler? Changed;

    public int GetPlayCount(string filePath) =>
        _entries.TryGetValue(filePath, out var entry) ? entry.PlayCount : 0;

    public DateTimeOffset? GetLastPlayed(string filePath) =>
        _entries.TryGetValue(filePath, out var entry) ? entry.LastPlayedUtc : null;

    public void RecordPlay(string filePath)
    {
        if (_entries.TryGetValue(filePath, out var entry))
        {
            entry.PlayCount++;
            entry.LastPlayedUtc = DateTimeOffset.UtcNow;
        }
        else
        {
            _entries[filePath] = new PlayHistoryEntry
            {
                FilePath = filePath,
                PlayCount = 1,
                LastPlayedUtc = DateTimeOffset.UtcNow
            };
        }

        Persist();
    }

    private void Persist()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            // Write-then-rename instead of overwriting in place, so a crash mid-write
            // can never leave a truncated/corrupt history file behind.
            var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_entries.Values.ToList(), JsonOptions));
            File.Move(temporaryPath, _filePath, true);
        }
        catch
        {
            // Play history is best-effort; a read-only app-data folder must not crash playback.
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var loaded = JsonSerializer.Deserialize<List<PlayHistoryEntry>>(File.ReadAllText(_filePath));
            if (loaded is null) return;

            foreach (var entry in loaded)
                _entries[entry.FilePath] = entry;
        }
        catch
        {
            // Ignore a corrupt or outdated history file.
        }
    }
}
