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
		builder.Services.AddSingleton<AudioPlayerService>();
		builder.Services.AddSingleton<PlaybackService>();
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
