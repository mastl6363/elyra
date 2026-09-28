using System.Diagnostics;
using System.Net.Sockets;
using Elyra;
using Elyra.Services;

const string Url = "http://127.0.0.1:5297";

// Elyra is a single-user local app. If it's already running (e.g. launched
// twice from the app menu), don't crash on the port conflict — just bring the
// existing instance's tab back up and exit.
if (IsAlreadyRunning())
{
    OpenBrowser(Url);
    return;
}

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
