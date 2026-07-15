using LibVLCSharp.Shared;

namespace Elyra.Services;

/// <summary>Selects a VLC runtime with DVD modules when one is installed on Windows.</summary>
public static class VlcRuntime
{
    private static readonly string[] WindowsCandidates =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN", "VLC"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VideoLAN", "VLC")
    ];

    public static string? SelectedPath { get; private set; }
    public static bool HasDvdSupport { get; private set; }

    public static void Initialize()
    {
        if (OperatingSystem.IsWindows())
        {
            var systemVlc = WindowsCandidates.FirstOrDefault(IsDvdCapableRuntime);
            if (systemVlc is not null)
            {
                Core.Initialize(systemVlc);
                SelectedPath = systemVlc;
                HasDvdSupport = true;
                return;
            }
        }

        Core.Initialize();
        SelectedPath = null;
        HasDvdSupport = false;
    }

    private static bool IsDvdCapableRuntime(string path) =>
        File.Exists(Path.Combine(path, "libvlc.dll"))
        && File.Exists(Path.Combine(path, "plugins", "access", "libdvdnav_plugin.dll"))
        && File.Exists(Path.Combine(path, "plugins", "access", "libdvdread_plugin.dll"));
}
