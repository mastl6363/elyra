using System.Text.Json;
using Elyra.Models;

namespace Elyra.Services;

public sealed class PlaybackSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly object _gate = new();

    public PlaybackSessionStore()
        : this(Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "playback-session.json")) { }

    public PlaybackSessionStore(string filePath) => _filePath = filePath;

    public PlaybackSessionState? Load()
    {
        lock (_gate)
        {
            try
            {
                return File.Exists(_filePath)
                    ? JsonSerializer.Deserialize<PlaybackSessionState>(File.ReadAllText(_filePath))
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }

    public void Save(PlaybackSessionState state)
    {
        lock (_gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(_filePath, JsonSerializer.Serialize(state, JsonOptions));
            }
            catch
            {
                // Playback persistence must never interrupt audio.
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(_filePath)) File.Delete(_filePath);
            }
            catch { }
        }
    }
}
