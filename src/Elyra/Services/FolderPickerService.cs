using System.Diagnostics;

namespace Elyra.Services;

/// <summary>
/// Cross-platform folder selection. Windows uses the native WinUI picker; the Linux
/// desktop host shells out to zenity (the standard GTK dialog tool). Android/iOS
/// pickers follow once those targets are built.
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
#elif ANDROID || IOS || MACCATALYST
        await Task.CompletedTask;
        throw new PlatformNotSupportedException(
            "Ordnerauswahl ist auf dieser Plattform noch nicht implementiert.");
#else
        return await PickFolderWithZenityAsync();
#endif
    }

#if !(WINDOWS || ANDROID || IOS || MACCATALYST)
    private static async Task<string?> PickFolderWithZenityAsync()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "zenity",
            ArgumentList = { "--file-selection", "--directory", "--title=Musikordner auswählen" },
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            // Exit code 1 means the user cancelled the dialog — not an error.
            return process.ExitCode == 0 ? output.TrimEnd('\n') : null;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new PlatformNotSupportedException(
                "Ordnerauswahl benötigt 'zenity' (sudo apt install zenity).", ex);
        }
    }
#endif
}
