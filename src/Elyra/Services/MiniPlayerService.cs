namespace Elyra.Services;

/// <summary>Switches the shared UI and desktop window between full and compact playback modes.</summary>
public sealed class MiniPlayerService
{
    private const double MiniWidth = 460;
    private const double MiniHeight = 220;
    private double _restoreWidth = 1100;
    private double _restoreHeight = 720;

    public bool IsMiniPlayer { get; private set; }
    public event EventHandler? Changed;

    public void Enter()
    {
        if (IsMiniPlayer)
            return;
        IsMiniPlayer = true;
        ResizeWindow(true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Exit()
    {
        if (!IsMiniPlayer)
            return;
        IsMiniPlayer = false;
        ResizeWindow(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Toggle()
    {
        if (IsMiniPlayer) Exit(); else Enter();
    }

    private void ResizeWindow(bool compact)
    {
        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
        {
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window is null)
                return;

            if (compact)
            {
                if (double.IsFinite(window.Width) && window.Width > MiniWidth)
                    _restoreWidth = window.Width;
                if (double.IsFinite(window.Height) && window.Height > MiniHeight)
                    _restoreHeight = window.Height;
            }

#if WINDOWS
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow
                && nativeWindow.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.Restore();
                presenter.IsAlwaysOnTop = compact;
                presenter.IsMaximizable = !compact;
            }
#endif

            if (compact)
            {
                window.MinimumWidth = 380;
                window.MinimumHeight = 190;
                window.MaximumWidth = 640;
                window.MaximumHeight = 360;
                window.Width = MiniWidth;
                window.Height = MiniHeight;
            }
            else
            {
                window.MinimumWidth = 0;
                window.MinimumHeight = 0;
                window.MaximumWidth = double.PositiveInfinity;
                window.MaximumHeight = double.PositiveInfinity;
                window.Width = Math.Max(760, _restoreWidth);
                window.Height = Math.Max(520, _restoreHeight);
            }
        });
    }
}
