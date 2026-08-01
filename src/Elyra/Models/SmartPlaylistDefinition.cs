namespace Elyra.Models;

public sealed record SmartPlaylistDefinition(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<Track> Tracks,
    string Accent);
