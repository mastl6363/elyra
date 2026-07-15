using Elyra.Services;
using Microsoft.Extensions.Logging;

namespace Elyra;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		// Load the native libVLC binaries once, before any LibVLC object is created.
		LibVLCSharp.Shared.Core.Initialize();

		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
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

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
