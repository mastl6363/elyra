using Elyra.Models;
using SQLite;

namespace Elyra.Services;

/// <summary>
/// SQLite-backed library index. Existing JSON snapshots are imported once so
/// upgrades retain the selected folder and all cached metadata.
/// </summary>
public sealed class SqliteLibraryStateStore : ILibraryStateStore, IDisposable
{
    private readonly SQLiteConnection _database;
    private readonly ILibraryStateStore? _legacyStore;

    public SqliteLibraryStateStore()
        : this(
            Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "library.db3"),
            new JsonLibraryStateStore())
    {
    }

    public SqliteLibraryStateStore(string databasePath, ILibraryStateStore? legacyStore = null)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        _database = new SQLiteConnection(
            databasePath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
        _database.CreateTable<LibraryMetaRow>();
        _database.CreateTable<LibraryTrackRow>();
        _database.ExecuteScalar<string>("PRAGMA journal_mode=WAL");
        _legacyStore = legacyStore;
    }

    public LibraryState? Load()
    {
        var meta = _database.Find<LibraryMetaRow>(1);
        if (meta is null)
        {
            var legacy = _legacyStore?.Load();
            if (legacy is null)
                return null;

            Save(legacy);
            return legacy;
        }

        return new LibraryState
        {
            MetadataVersion = meta.MetadataVersion,
            FolderPath = string.IsNullOrWhiteSpace(meta.FolderPath) ? null : meta.FolderPath,
            Tracks = _database.Table<LibraryTrackRow>()
                .OrderBy(row => row.Artist)
                .ThenBy(row => row.Album)
                .ThenBy(row => row.TrackNumber)
                .ToList()
                .Select(ToTrack)
                .ToList()
        };
    }

    public void Save(LibraryState state)
    {
        _database.RunInTransaction(() =>
        {
            _database.DeleteAll<LibraryTrackRow>();
            _database.InsertAll(state.Tracks.Select(FromTrack));
            _database.InsertOrReplace(new LibraryMetaRow
            {
                Id = 1,
                MetadataVersion = state.MetadataVersion,
                FolderPath = state.FolderPath ?? ""
            });
        });
    }

    public void Clear()
    {
        _database.RunInTransaction(() =>
        {
            _database.DeleteAll<LibraryTrackRow>();
            _database.DeleteAll<LibraryMetaRow>();
        });
        _legacyStore?.Clear();
    }

    private static LibraryTrackRow FromTrack(Track track) => new()
    {
        FilePath = track.FilePath,
        Title = track.Title,
        Artist = track.Artist,
        Album = track.Album,
        AlbumArtist = track.AlbumArtist,
        Genre = track.Genre,
        Year = track.Year,
        TrackNumber = track.TrackNumber,
        DiscNumber = track.DiscNumber,
        DurationTicks = track.Duration.Ticks,
        CoverArtDataUri = track.CoverArtDataUri
    };

    private static Track ToTrack(LibraryTrackRow row) => new()
    {
        FilePath = row.FilePath,
        Title = row.Title,
        Artist = row.Artist,
        Album = row.Album,
        AlbumArtist = row.AlbumArtist,
        Genre = row.Genre,
        Year = (uint)Math.Max(0, row.Year),
        TrackNumber = (uint)Math.Max(0, row.TrackNumber),
        DiscNumber = (uint)Math.Max(0, row.DiscNumber),
        Duration = TimeSpan.FromTicks(Math.Max(0, row.DurationTicks)),
        CoverArtDataUri = row.CoverArtDataUri
    };

    public void Dispose() => _database.Dispose();

    [Table("LibraryMeta")]
    public sealed class LibraryMetaRow
    {
        [PrimaryKey] public int Id { get; set; }
        public int MetadataVersion { get; set; }
        public string FolderPath { get; set; } = "";
    }

    [Table("LibraryTracks")]
    public sealed class LibraryTrackRow
    {
        [PrimaryKey] public string FilePath { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string AlbumArtist { get; set; } = "";
        public string Genre { get; set; } = "";
        public long Year { get; set; }
        public long TrackNumber { get; set; }
        public long DiscNumber { get; set; }
        public long DurationTicks { get; set; }
        public string? CoverArtDataUri { get; set; }
    }
}
