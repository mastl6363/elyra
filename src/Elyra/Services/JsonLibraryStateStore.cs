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
        : this(Path.Combine(AppPaths.DataDirectory, "library.json"))
    {
    }

    public JsonLibraryStateStore(string filePath) => _filePath = filePath;

    public LibraryState? Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return null;
            using var stream = File.OpenRead(_filePath);
            return JsonSerializer.Deserialize<LibraryState>(stream, JsonOptions);
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

            // A unique suffix (rather than a fixed ".tmp" name) means two overlapping
            // Save() calls can't collide on the same temp file and make one File.Move
            // fail while the other's write is silently lost.
            var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var stream = File.Create(temporaryPath))
                    JsonSerializer.Serialize(stream, state, JsonOptions);
                File.Move(temporaryPath, _filePath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
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
