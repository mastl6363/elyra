using System.Diagnostics;
#if ANDROID
using Android.Provider;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
#endif

namespace Elyra.Services;

/// <summary>
/// Cross-platform folder selection. Windows uses the native WinUI picker; the Linux
/// desktop host shells out to zenity (the standard GTK dialog tool). Android
/// uses the system document-tree picker and copies supported music into app data.
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
#elif ANDROID
        var activity = Platform.CurrentActivity as MainActivity
            ?? throw new InvalidOperationException("Das Android-Hauptfenster ist nicht verfügbar.");
        var uri = await MainThread.InvokeOnMainThreadAsync(activity.PickMusicFolderAsync);
        if (uri is null) return null;
        var path = await Task.Run(() => CopyAndroidMusicTree(uri));
        Preferences.Default.Set(AndroidTreePreference, uri.ToString());
        return path;
#elif IOS || MACCATALYST
        await Task.CompletedTask;
        throw new PlatformNotSupportedException(
            "Ordnerauswahl ist auf dieser Plattform noch nicht implementiert.");
#else
        return await PickFolderWithZenityAsync();
#endif
    }

    public Task<string> PrepareRefreshAsync(string folderPath)
    {
#if ANDROID
        if (string.Equals(folderPath, AndroidImportPath, StringComparison.Ordinal))
        {
            var savedUri = Preferences.Default.Get(AndroidTreePreference, "");
            if (!string.IsNullOrWhiteSpace(savedUri))
                return Task.Run(() => CopyAndroidMusicTree(Android.Net.Uri.Parse(savedUri)!));
        }
#endif
        return Task.FromResult(folderPath);
    }

#if ANDROID
    private const string AndroidTreePreference = "android_music_tree_uri";
    private static string AndroidImportPath => Path.Combine(AppPaths.DataDirectory, "ImportedMusic");
    private static readonly HashSet<string> AndroidAudioExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".flac", ".ogg" };

    private static string CopyAndroidMusicTree(Android.Net.Uri treeUri)
    {
        var resolver = Android.App.Application.Context.ContentResolver
            ?? throw new IOException("Android-Dateizugriff ist nicht verfügbar.");
        var destination = AndroidImportPath;
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var backup = destination + "." + Guid.NewGuid().ToString("N") + ".bak";
        Directory.CreateDirectory(temporary);
        try
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var rootId = DocumentsContract.GetTreeDocumentId(treeUri)
                ?? throw new IOException("Der ausgewählte Ordner konnte nicht geöffnet werden.");
            var copied = CopyDocuments(rootId, temporary, treeUri, resolver, visited);
            if (copied == 0)
                throw new IOException("In diesem Ordner wurden keine MP3-, FLAC- oder OGG-Dateien gefunden.");
            if (Directory.Exists(destination)) Directory.Move(destination, backup);
            try { Directory.Move(temporary, destination); }
            catch
            {
                if (Directory.Exists(backup)) Directory.Move(backup, destination);
                throw;
            }
            try { if (Directory.Exists(backup)) Directory.Delete(backup, true); }
            catch { /* new import is already available; stale backup can be removed later */ }
            return destination;
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }

    private static int CopyDocuments(string parentId, string destination,
        Android.Net.Uri treeUri, Android.Content.ContentResolver resolver, HashSet<string> visited)
    {
        if (!visited.Add(parentId)) return 0;
        if (visited.Count > 10000) throw new IOException("Der Musikordner enthält zu viele Unterordner.");
        var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, parentId)
            ?? throw new IOException("Der Musikordner konnte nicht gelesen werden.");
        string[] columns =
        [
            DocumentsContract.Document.ColumnDocumentId,
            DocumentsContract.Document.ColumnDisplayName,
            DocumentsContract.Document.ColumnMimeType
        ];
        using var cursor = resolver.Query(childrenUri, columns, null, null, null)
            ?? throw new IOException("Der Musikordner konnte nicht gelesen werden.");
        var idColumn = cursor.GetColumnIndexOrThrow(DocumentsContract.Document.ColumnDocumentId);
        var nameColumn = cursor.GetColumnIndexOrThrow(DocumentsContract.Document.ColumnDisplayName);
        var mimeColumn = cursor.GetColumnIndexOrThrow(DocumentsContract.Document.ColumnMimeType);
        var count = 0;
        while (cursor.MoveToNext())
        {
            var id = cursor.GetString(idColumn);
            var name = cursor.GetString(nameColumn);
            var mime = cursor.GetString(mimeColumn);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;
            var safeName = name.Replace('/', '_').Replace('\\', '_');
            if (safeName is "." or "..") continue;
            var target = Path.Combine(destination, safeName);
            if (mime == "vnd.android.document/directory")
            {
                Directory.CreateDirectory(target);
                count += CopyDocuments(id, target, treeUri, resolver, visited);
            }
            else if (AndroidAudioExtensions.Contains(Path.GetExtension(safeName)))
            {
                var documentUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, id)
                    ?? throw new IOException($"{name} konnte nicht geöffnet werden.");
                using var input = resolver.OpenInputStream(documentUri)
                    ?? throw new IOException($"{name} konnte nicht gelesen werden.");
                using var output = File.Create(target);
                input.CopyTo(output);
                count++;
            }
        }
        return count;
    }
#endif

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
