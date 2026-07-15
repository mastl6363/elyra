namespace Elyra.Models;

/// <summary>Serializable snapshot used to restore the local library on startup.</summary>
public sealed class LibraryState
{
    public string? FolderPath { get; set; }
    public List<Track> Tracks { get; set; } = new();
}
