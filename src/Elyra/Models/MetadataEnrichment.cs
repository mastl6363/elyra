namespace Elyra.Models;

public sealed record TrackMetadataUpdate(string FilePath, string Album, string AlbumArtist);

public sealed record MetadataEnrichmentProgress(
    int Completed,
    int Total,
    int Matched,
    int Skipped,
    int Failed,
    string CurrentTrack);

public sealed record MetadataEnrichmentResult(
    IReadOnlyList<TrackMetadataUpdate> Updates,
    int Matched,
    int Skipped,
    int Failed);
