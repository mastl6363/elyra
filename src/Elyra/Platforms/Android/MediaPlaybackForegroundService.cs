using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using Elyra.Services;
using Microsoft.Extensions.DependencyInjection;
using Application = Android.App.Application;
using Notification = Android.App.Notification;

namespace Elyra;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public sealed class MediaPlaybackForegroundService : Service
{
    private const string ChannelId = "elyra.playback";
    private const int NotificationId = 1;
    private const string PauseAction = "com.mastl.elyra.playback.PAUSE";
    private const string PreviousAction = "com.mastl.elyra.playback.PREVIOUS";
    private const string NextAction = "com.mastl.elyra.playback.NEXT";

    private PlaybackService _playback = null!;
    private AudioPlayerService _audio = null!;
    private NotificationManager _notifications = null!;
    private MediaSession _session = null!;
    private TransportCallback _transport = null!;
    private Handler _main = null!;
    private PowerManager.WakeLock _wakeLock = null!;
    private int _startId;
    private bool _foreground;

    internal static void Start()
    {
        var context = Application.Context;
        using var intent = new Intent(context, typeof(MediaPlaybackForegroundService));
        if (OperatingSystem.IsAndroidVersionAtLeast(26)) context.StartForegroundService(intent);
        else context.StartService(intent);
    }

    public override void OnCreate()
    {
        base.OnCreate();
        var services = IPlatformApplication.Current!.Services;
        _playback = services.GetRequiredService<PlaybackService>();
        _audio = services.GetRequiredService<AudioPlayerService>();
        _main = new Handler(Looper.MainLooper!);
        _notifications = (NotificationManager)GetSystemService(NotificationService)!;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            using var channel = new NotificationChannel(ChannelId, "Musikwiedergabe", NotificationImportance.Low);
            _notifications.CreateNotificationChannel(channel);
        }

        _session = new MediaSession(this, "Elyra");
        _transport = new TransportCallback(_playback);
        _session.SetCallback(_transport, _main);
        var power = (PowerManager)GetSystemService(PowerService)!;
        _wakeLock = power.NewWakeLock(WakeLockFlags.Partial, "Elyra:MusicPlayback")!;
        _wakeLock.SetReferenceCounted(false);
        _playback.CurrentChanged += OnPlaybackChanged;
        _audio.StateChanged += OnPlaybackChanged;
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        _startId = startId;
        if (!_foreground)
        {
            // Fulfill StartForegroundService immediately, even if playback has already failed.
            _session.Active = true;
            using var notification = CreateNotification("Elyra", "Wiedergabe wird gestartet", false, false);
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                StartForeground(NotificationId, notification, ForegroundService.TypeMediaPlayback);
            else
                StartForeground(NotificationId, notification);
            _foreground = true;
        }

        if (_playback.GetAudioSession().Requested)
        {
            switch (intent?.Action)
            {
                case PauseAction:
                    _transport.OnPause();
                    break;
                case PreviousAction:
                    _transport.OnSkipToPrevious();
                    break;
                case NextAction:
                    _transport.OnSkipToNext();
                    break;
            }
        }
        Refresh();
        return StartCommandResult.NotSticky;
    }

    private void OnPlaybackChanged(object? sender, EventArgs args) => _main.Post(Refresh);

    private void Refresh()
    {
        if (!_foreground) return;
        var source = _playback.GetAudioSession();
        if (!source.Requested)
        {
            ReleaseWakeLock();
            // A new play request may already be queued behind this callback.
            StopSelfResult(_startId);
            return;
        }

        if (!_wakeLock.IsHeld) _wakeLock.Acquire();
        var title = source.Track?.Title ?? source.Station?.Name ?? "Elyra";
        var artist = source.Track?.Artist ?? string.Empty;
        var playing = _playback.IsPlaying;
        var canGoPrevious = source.Track is not null;
        var actions = PlaybackState.ActionPause | PlaybackState.ActionStop;
        if (canGoPrevious) actions |= PlaybackState.ActionSkipToPrevious;
        if (source.HasNext) actions |= PlaybackState.ActionSkipToNext;
        using var metadataBuilder = new MediaMetadata.Builder();
        metadataBuilder.PutString(MediaMetadata.MetadataKeyTitle, title);
        metadataBuilder.PutString(MediaMetadata.MetadataKeyArtist, artist);
        using var metadata = metadataBuilder.Build();
        _session.SetMetadata(metadata);

        using var stateBuilder = new PlaybackState.Builder();
        stateBuilder.SetActions(actions);
        stateBuilder.SetState(playing ? PlaybackStateCode.Playing : PlaybackStateCode.Buffering,
            (long)_playback.Position.TotalMilliseconds, playing ? 1f : 0f);
        using var state = stateBuilder.Build();
        _session.SetPlaybackState(state);

        using var notification = CreateNotification(title, artist, canGoPrevious, source.HasNext);
        _notifications.Notify(NotificationId, notification);
    }

    private Notification CreateNotification(string title, string artist, bool canGoPrevious, bool canGoNext)
    {
        using var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, ChannelId)
            : new Notification.Builder(this);
        using var appIntent = new Intent(this, typeof(MainActivity));
        appIntent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        using var openApp = PendingIntent.GetActivity(this, 0, appIntent, PendingIntentFlags.Immutable);
        using var style = new Notification.MediaStyle();
        using var token = _session.SessionToken;
        style.SetMediaSession(token);
        builder.SetStyle(style)
            .SetContentIntent(openApp)
            .SetSmallIcon(Android.Resource.Drawable.IcMediaPlay)
            .SetContentTitle(title)
            .SetContentText(artist)
            .SetOnlyAlertOnce(true)
            .SetOngoing(true)
            .SetShowWhen(false);

        if (canGoPrevious)
            AddAction(builder, Android.Resource.Drawable.IcMediaPrevious, "Zurück", PreviousAction);
        AddAction(builder, Android.Resource.Drawable.IcMediaPause, "Pause", PauseAction);
        if (canGoNext)
            AddAction(builder, Android.Resource.Drawable.IcMediaNext, "Weiter", NextAction);
        style.SetShowActionsInCompactView(Enumerable.Range(0, 1 + (canGoPrevious ? 1 : 0) + (canGoNext ? 1 : 0)).ToArray());
        return builder.Build()!;
    }

    private void AddAction(Notification.Builder builder, int icon, string label, string action)
    {
        using var intent = new Intent(this, typeof(MediaPlaybackForegroundService));
        intent.SetAction(action);
        using var pending = PendingIntent.GetService(this, 0, intent, PendingIntentFlags.Immutable);
        using var drawable = Icon.CreateWithResource(this, icon);
        using var actionBuilder = new Notification.Action.Builder(drawable, label, pending);
        using var notificationAction = actionBuilder.Build();
        builder.AddAction(notificationAction);
    }

    private void ReleaseWakeLock()
    {
        if (_wakeLock.IsHeld) _wakeLock.Release();
    }

    public override void OnDestroy()
    {
        _foreground = false;
        _playback.CurrentChanged -= OnPlaybackChanged;
        _audio.StateChanged -= OnPlaybackChanged;
        _main.RemoveCallbacksAndMessages(null);
        ReleaseWakeLock();
        _wakeLock.Dispose();
        _session.Active = false;
        _session.SetCallback(null);
        _session.Release();
        _transport.Dispose();
        _session.Dispose();
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }

    private sealed class TransportCallback(PlaybackService playback) : MediaSession.Callback
    {
        public override void OnPause() => playback.SetAudioSessionPaused(true);
        public override void OnStop() => playback.StopAudioSession();

        public override void OnSkipToPrevious()
        {
            if (playback.GetAudioSession().Requested) playback.Previous();
        }

        public override void OnSkipToNext()
        {
            if (playback.GetAudioSession().Requested) playback.Next();
        }
    }
}
