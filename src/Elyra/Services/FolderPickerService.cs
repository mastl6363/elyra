namespace Elyra.Services;

/// <summary>
/// Cross-platform folder selection. Only the Windows path is implemented for the
/// Phase 1 MVP; Android/iOS pickers follow once those targets are built.
/// </summary>
public sealed class FolderPickerService
{
    public async Task<string?> PickFolderAsync()
    {
#if WINDOWS
        var picker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary
        };
        picker.FileTypeFilter.Add("*");

        // An unpackaged WinUI 3 picker must be associated with the app's window handle.
        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
        if (window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window platformWindow)
            return null;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(platformWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException(
            "Ordnerauswahl ist auf dieser Plattform noch nicht implementiert.");
#endif
    }
}
