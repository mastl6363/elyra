using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace Elyra;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const int MusicFolderRequest = 4107;
    private TaskCompletionSource<Android.Net.Uri?>? _musicFolderRequest;

    public Task<Android.Net.Uri?> PickMusicFolderAsync()
    {
        if (_musicFolderRequest is not null)
            throw new InvalidOperationException("Die Ordnerauswahl ist bereits geöffnet.");

        var completion = new TaskCompletionSource<Android.Net.Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _musicFolderRequest = completion;
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission |
                        ActivityFlags.GrantPersistableUriPermission |
                        ActivityFlags.GrantPrefixUriPermission);
        try { StartActivityForResult(intent, MusicFolderRequest); }
        catch (Exception exception)
        {
            _musicFolderRequest = null;
            completion.TrySetException(exception);
        }
        return completion.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != MusicFolderRequest) return;
        var completion = _musicFolderRequest;
        _musicFolderRequest = null;
        if (completion is null) return;
        var uri = resultCode == Result.Ok ? data?.Data : null;
        if (uri is null) { completion.TrySetResult(null); return; }
        try
        {
            ContentResolver!.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission);
            completion.TrySetResult(uri);
        }
        catch (Exception exception) { completion.TrySetException(exception); }
    }
}
