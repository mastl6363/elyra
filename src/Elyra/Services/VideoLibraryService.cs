using System.Text.Json;
using Elyra.Models;

namespace Elyra.Services;

/// <summary>Manages local movies, optical drives and persisted resume positions.</summary>
public sealed class VideoLibraryService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".m4v", ".mpg", ".mpeg", ".ts", ".m2ts", ".wmv"
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly List<VideoItem> _videos = [];

    public VideoLibraryService()
        : this(Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "videos.json")) { }

    public VideoLibraryService(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public IReadOnlyList<VideoItem> Videos => _videos;
    public event EventHandler? Changed;

    public async Task<int> PickAndAddAsync()
    {
        var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.WinUI] = SupportedExtensions,
            [DevicePlatform.Android] = ["video/*"],
            [DevicePlatform.iOS] = ["public.movie"],
            [DevicePlatform.MacCatalyst] = ["public.movie"]
        });

        var results = await FilePicker.Default.PickMultipleAsync(new PickOptions
        {
            PickerTitle = "Filme auswählen",
            FileTypes = fileTypes
        });

        return AddFiles(results.Select(result => result?.FullPath).OfType<string>());
    }

    public int AddFiles(IEnumerable<string> filePaths)
    {
        var added = 0;
        foreach (var path in filePaths.Where(IsSupportedFile).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_videos.Any(video => string.Equals(video.Source, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            _videos.Add(new VideoItem
            {
                Source = path,
                Title = HumanizeTitle(Path.GetFileNameWithoutExtension(path)),
                Kind = VideoSourceKind.File
            });
            added++;
        }

        if (added > 0) Persist();
        return added;
    }

    public void Remove(string id)
    {
        if (_videos.RemoveAll(video => video.Id == id) > 0)
            Persist();
    }

    public void SavePosition(string id, TimeSpan position, TimeSpan duration)
    {
        var video = _videos.FirstOrDefault(item => item.Id == id);
        if (video is null) return;

        // Near the credits/end, restart the movie next time instead of resuming.
        var completed = duration > TimeSpan.Zero && duration - position < TimeSpan.FromSeconds(30);
        video.PositionMs = completed ? 0 : Math.Max(0, (long)position.TotalMilliseconds);
        video.LastPlayedAt = DateTimeOffset.UtcNow;
        Persist(false);
    }

    public IReadOnlyList<VideoItem> GetDvdDrives()
    {
        if (!OperatingSystem.IsWindows()) return [];

        var drives = new List<VideoItem>();
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.CDRom))
        {
            try
            {
                var ready = drive.IsReady;
                drives.Add(new VideoItem
                {
                    Id = $"dvd-{drive.Name}",
                    Source = drive.RootDirectory.FullName,
                    Title = ready && !string.IsNullOrWhiteSpace(drive.VolumeLabel)
                        ? drive.VolumeLabel
                        : $"DVD-Laufwerk {drive.Name.TrimEnd('\\')}",
                    Kind = VideoSourceKind.Dvd,
                    IsAvailable = ready
                });
            }
            catch
            {
                // A drive can disappear while Windows is enumerating it.
            }
        }

        return drives;
    }

    public static bool IsSupportedFile(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && File.Exists(path)
        && SupportedExtensions.Contains(Path.GetExtension(path));

    private static string HumanizeTitle(string fileName) =>
        string.Join(' ', fileName.Replace('.', ' ').Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private void Persist(bool notify = true)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_videos, JsonOptions));
        }
        catch { }

        if (notify) Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var loaded = JsonSerializer.Deserialize<List<VideoItem>>(File.ReadAllText(_filePath));
            if (loaded is not null)
                _videos.AddRange(loaded.Where(video => IsSupportedFile(video.Source)));
        }
        catch { }
    }
}
