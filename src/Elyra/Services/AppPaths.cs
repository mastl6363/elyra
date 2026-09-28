namespace Elyra.Services;

/// <summary>
/// Cross-platform app-data directory. On MAUI targets (Windows/Android/iOS/MacCatalyst)
/// this defers to the sandbox-correct MAUI path; on the plain-.NET Linux desktop host
/// (no Maui platform symbol defined) it falls back to the XDG data directory so the two
/// projects can share the same Services code without a MAUI reference.
/// </summary>
public static class AppPaths
{
#if ANDROID || IOS || MACCATALYST || WINDOWS
    public static string DataDirectory => Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
#else
    public static string DataDirectory { get; } = CreateDesktopDataDirectory();

    private static string CreateDesktopDataDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Elyra");
        Directory.CreateDirectory(dir);
        return dir;
    }
#endif
}
