using System.Text.Json;
using Elyra.Models;

namespace Elyra.Services;

/// <summary>Persists the Phase-1 library snapshot in the app-data directory.</summary>
public sealed class JsonLibraryStateStore : ILibraryStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        IgnoreReadOnlyProperties = true
    };
    private readonly string _filePath;

    public JsonLibraryStateStore()
        : this(Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "library.json"))
    {
    }

    public JsonLibraryStateStore(string filePath) => _filePath = filePath;

    public LibraryState? Load()
    {
        try
        {
            return File.Exists(_filePath)
                ? JsonSerializer.Deserialize<LibraryState>(File.ReadAllText(_filePath), JsonOptions)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public void Save(LibraryState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = _filePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temporaryPath, _filePath, true);
        }
        catch
        {
            // Persistence is best-effort; a read-only app-data folder must not break playback.
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
        catch
        {
            // Best-effort cleanup, consistent with Save/Load.
        }
    }
}
