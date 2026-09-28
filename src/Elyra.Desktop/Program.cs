using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using Elyra;
using Elyra.Services;

const string Url = "http://127.0.0.1:5297";

// Elyra is a single-user local app. If it's already running (e.g. launched
// twice from the app menu), don't crash on the port conflict — just bring the
// existing instance's tab back up and exit. But if that running instance is an
// older build (e.g. `apt upgrade`/`dpkg -i` replaced the binary on disk while
// the old process was still running — its file handle stays valid, so it
// keeps serving the old code from memory), kill it first so the freshly
// installed version actually takes over instead of the upgrade silently
// doing nothing from the user's point of view.
var pidFilePath = Path.Combine(AppPaths.DataDirectory, "desktop-instance.pid");
var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
var exeVersion = File.Exists(exePath) ? File.GetLastWriteTimeUtc(exePath).ToString("O") : "";

if (IsAlreadyRunning())
{
    if (TryReplaceStaleInstance(pidFilePath, exePath, exeVersion))
    {
        // Old instance killed — fall through and start fresh below.
    }
    else
    {
        OpenBrowser(Url);
        return;
    }
}

File.WriteAllText(pidFilePath, $"{Environment.ProcessId}|{exePath}|{exeVersion}");

// Load the native libVLC binaries once, before any LibVLC object is created,
// mirroring MauiProgram.cs on the MAUI targets.
VlcRuntime.Initialize();

// Content root defaults to the process's current working directory, which for a
// double-clicked/launched executable is unpredictable. Pin it to the app's own
// output directory so wwwroot (css, static assets) resolves regardless of the
// working directory the app was started from.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<AudioPlayerService>();
builder.Services.AddSingleton<PlaybackService>();
builder.Services.AddSingleton<PlayHistoryService>();
builder.Services.AddSingleton<SuggestionService>();
builder.Services.AddSingleton<ILibraryStateStore, JsonLibraryStateStore>();
builder.Services.AddSingleton<MusicLibraryService>();
builder.Services.AddSingleton<MusicBrainzMetadataService>();
builder.Services.AddSingleton<FolderPickerService>();
builder.Services.AddSingleton<PlaylistService>();
builder.Services.AddSingleton<RadioBrowserService>();
builder.Services.AddSingleton<RadioFavoritesService>();
builder.Services.AddSingleton<VideoLibraryService>();
builder.Services.AddSingleton<VideoPlaybackService>();

// Elyra is a single-user local app; bind to loopback only so it's never
// reachable from the network, on a fixed port so the browser URL is predictable.
builder.WebHost.UseUrls(Url);

var app = builder.Build();

app.MapStaticAssets();
app.UseAntiforgery();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

Console.WriteLine($"Elyra läuft auf {Url}");
OpenBrowser(Url);

app.Run();

// Returns true (having killed the old process and freed the port) only when
// the pidfile identifies a live process that was started from a now-outdated
// build of this same executable. Anything less certain — no pidfile, process
// gone, or it's genuinely today's build already running — is left alone.
static bool TryReplaceStaleInstance(string pidFilePath, string currentExePath, string currentExeVersion)
{
    string[] parts;
    try
    {
        if (!File.Exists(pidFilePath)) return false;
        parts = File.ReadAllText(pidFilePath).Split('|', 3);
        if (parts.Length != 3) return false;
    }
    catch { return false; }

    if (!int.TryParse(parts[0], out var pid)) return false;
    var (recordedExePath, recordedVersion) = (parts[1], parts[2]);

    // Same build already running (or we can't tell) — treat as up to date.
    if (recordedExePath != currentExePath || recordedVersion == currentExeVersion)
        return false;

    try
    {
        var process = Process.GetProcessById(pid);
        // Only kill it if it's still the process we think it is, identified by
        // the same recorded executable path — avoids killing an unrelated
        // process that happens to have reused this PID since.
        if (process.MainModule?.FileName != recordedExePath && !IsDeletedExePath(pid, recordedExePath))
            return false;

        process.Kill();
        process.WaitForExit((int)TimeSpan.FromSeconds(5).TotalMilliseconds);
    }
    catch
    {
        return false; // process already gone, or we don't have permission — leave it
    }

    // Wait for the port to actually free up before the caller starts a new listener.
    for (var i = 0; i < 25 && IsAlreadyRunning(); i++)
        Thread.Sleep(200);

    return true;

    static bool IsDeletedExePath(int pid, string expectedPath)
    {
        // On Linux, MainModule.FileName is null/empty once the on-disk file has
        // been replaced (dpkg -i) — /proc/<pid>/exe still resolves, just with
        // " (deleted)" appended, so compare against that instead.
        try
        {
            var link = new FileInfo($"/proc/{pid}/exe");
            return link.LinkTarget == expectedPath || link.LinkTarget == $"{expectedPath} (deleted)";
        }
        catch { return false; }
    }
}

static bool IsAlreadyRunning()
{
    try
    {
        using var probe = new TcpClient();
        return probe.ConnectAsync("127.0.0.1", 5297).Wait(TimeSpan.FromMilliseconds(200));
    }
    catch
    {
        return false;
    }
}

static void OpenBrowser(string url)
{
    // Prefer a Chromium-family browser's --app mode: it opens a chrome-less
    // window (no tabs, no address bar, just our own title bar) instead of a
    // regular browser tab, so Elyra looks and behaves like an installed app.
    // A dedicated --user-data-dir keeps this profile isolated from the user's
    // normal browsing session/history and lets Chrome reuse the same window
    // (rather than opening a duplicate) if Elyra is launched again.
    string[] chromiumCandidates =
    [
        "google-chrome-stable", "google-chrome", "chromium-browser", "chromium",
        "microsoft-edge-stable", "microsoft-edge"
    ];

    var profileDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Elyra", "app-window-profile");

    foreach (var candidate in chromiumCandidates)
    {
        if (TryStart(candidate,
            $"--app={url}", $"--user-data-dir={profileDir}", "--window-size=1400,900",
            "--no-first-run", "--no-default-browser-check",
            // Without this, Chrome's app window keeps its own WM_CLASS
            // ("Google-chrome"), so the taskbar/dock shows Chrome's icon
            // instead of Elyra's. --class makes the window report WM_CLASS
            // "Elyra", matching elyra.desktop's StartupWMClass so the desktop
            // environment picks up our icon instead.
            "--class=Elyra"))
            return;
    }

    // No Chromium-family browser found — fall back to the system default
    // browser, opened as a normal tab.
    TryStart("xdg-open", url);

    static bool TryStart(string fileName, params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo { FileName = fileName, UseShellExecute = false };
            foreach (var arg in arguments)
                info.ArgumentList.Add(arg);
            using var process = Process.Start(info);
            return process is not null;
        }
        catch
        {
            return false;
        }
    }
}
