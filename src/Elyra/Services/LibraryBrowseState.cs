namespace Elyra.Services;

public enum LibraryViewMode
{
    Songs,
    Artists,
    Albums
}

public enum TrackSortOrder
{
    Title,
    Artist,
    Album,
    Genre,
    Duration
}

public enum ArtistSortOrder
{
    Name,
    TrackCount,
    AlbumCount
}

public enum AlbumSortOrder
{
    Title,
    Artist,
    TrackCount
}

/// <summary>
/// Keeps the current library view while navigating to an artist or album and back.
/// The state is intentionally session-scoped; the persisted music library remains
/// independent from temporary browsing choices.
/// </summary>
public sealed class LibraryBrowseState
{
    public const string MissingGenreFilter = "__elyra_missing_genre__";

    public LibraryViewMode ViewMode { get; set; } = LibraryViewMode.Songs;
    public string SearchText { get; set; } = "";
    public string SelectedArtist { get; set; } = "";
    public string SelectedGenre { get; set; } = "";
    public AudioFileFilter FileFilter { get; set; } = AudioFileFilter.All;
    public TrackSortOrder TrackSort { get; set; } = TrackSortOrder.Title;
    public ArtistSortOrder ArtistSort { get; set; } = ArtistSortOrder.Name;
    public AlbumSortOrder AlbumSort { get; set; } = AlbumSortOrder.Title;
    public bool SortDescending { get; set; }
}
