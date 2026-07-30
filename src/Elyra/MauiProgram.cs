using Elyra.Services;
using LibVLCSharp.MAUI;
using Microsoft.Extensions.Logging;

namespace Elyra;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		// Load the native libVLC binaries once, before any LibVLC object is created.
		VlcRuntime.Initialize();

		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseLibVLCSharp()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

		// App services
		builder.Services.AddSingleton<PlaybackPreferencesService>();
		builder.Services.AddSingleton<AudioPlayerService>();
		builder.Services.AddSingleton<IAudioPlayerService>(services =>
			services.GetRequiredService<AudioPlayerService>());
		builder.Services.AddSingleton<PlaybackSessionStore>();
		builder.Services.AddSingleton<UserMusicDataService>();
		builder.Services.AddSingleton<PlaybackService>();
#if WINDOWS
		builder.Services.AddSingleton<ISystemMediaTransportService, WindowsSystemMediaTransportService>();
#else
		builder.Services.AddSingleton<ISystemMediaTransportService, NoOpSystemMediaTransportService>();
#endif
		builder.Services.AddSingleton<ILibraryStateStore, JsonLibraryStateStore>();
		builder.Services.AddSingleton<MusicLibraryService>();
		builder.Services.AddSingleton<MusicBrainzMetadataService>();
		builder.Services.AddSingleton<FolderPickerService>();
		builder.Services.AddSingleton<PlaylistService>();
		builder.Services.AddSingleton<RadioBrowserService>();
		builder.Services.AddSingleton<RadioFavoritesService>();
		builder.Services.AddSingleton<VideoLibraryService>();
		builder.Services.AddSingleton<VideoPlaybackService>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
