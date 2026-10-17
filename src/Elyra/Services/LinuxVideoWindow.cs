#if !ANDROID && !IOS && !MACCATALYST && !WINDOWS
using System.Runtime.InteropServices;

namespace Elyra.Services;

/// <summary>An X11 window that stays alive while LibVLC renders into its drawable.</summary>
internal sealed class LinuxVideoWindow : IDisposable
{
    private const long KeyPressMask = 1L << 0;
    private const long StructureNotifyMask = 1L << 17;
    private const int KeyPress = 2;
    private const int ClientMessage = 33;
    private readonly Action _onClose;
    private readonly Action<ulong> _onKey;
    private readonly Thread _events;
    private readonly nint _display;
    private readonly nint _window;
    private readonly nint _deleteAtom;
    private int _disposed;

    public static void InitializeXlib()
    {
        // LibVLC requires this before creating a LibVLC instance or opening an X display.
        if (XInitThreads() == 0)
            throw new InvalidOperationException("X11 konnte nicht für Video initialisiert werden.");
    }

    public LinuxVideoWindow(string title, Action onClose, Action<ulong> onKey)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
            throw new PlatformNotSupportedException("Für Videowiedergabe wird eine X11-Sitzung benötigt.");

        _onClose = onClose;
        _onKey = onKey;
        _display = XOpenDisplay(nint.Zero);
        if (_display == nint.Zero)
            throw new InvalidOperationException("Die X11-Anzeige konnte nicht geöffnet werden.");

        try
        {
            _window = XCreateSimpleWindow(_display, XDefaultRootWindow(_display),
                80, 80, 1120, 630, 0, 0, 0);
            if (_window == nint.Zero)
                throw new InvalidOperationException("Das Videofenster konnte nicht erstellt werden.");

            XStoreName(_display, _window, $"Elyra — {title}");
            XSelectInput(_display, _window, KeyPressMask | StructureNotifyMask);
            _deleteAtom = XInternAtom(_display, "WM_DELETE_WINDOW", false);
            var atom = _deleteAtom;
            XSetWMProtocols(_display, _window, ref atom, 1);
            XMapRaised(_display, _window);
            XFlush(_display);
        }
        catch
        {
            if (_window != nint.Zero) XDestroyWindow(_display, _window);
            XCloseDisplay(_display);
            throw;
        }

        _events = new Thread(ProcessEvents) { IsBackground = true, Name = "Elyra X11 video" };
        _events.Start();
    }

    public uint WindowId => unchecked((uint)(long)_window);

    public void Raise()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        XMapRaised(_display, _window);
        XFlush(_display);
    }

    private void ProcessEvents()
    {
        // XEvent is a union whose largest member is 24 native longs (192 bytes on x64).
        var buffer = Marshal.AllocHGlobal(192);
        try
        {
            while (Volatile.Read(ref _disposed) == 0)
            {
                if (XPending(_display) == 0)
                {
                    Thread.Sleep(25);
                    continue;
                }

                XNextEvent(_display, buffer);
                var type = Marshal.ReadInt32(buffer);
                if (type == ClientMessage && Marshal.ReadIntPtr(buffer, 56) == _deleteAtom)
                {
                    ThreadPool.QueueUserWorkItem(_ => InvokeSafely(_onClose));
                    return;
                }
                if (type == KeyPress)
                {
                    var key = unchecked((ulong)(long)XLookupKeysym(buffer, 0));
                    ThreadPool.QueueUserWorkItem(_ => InvokeSafely(() => _onKey(key)));
                }
            }
        }
        catch (Exception)
        {
            ThreadPool.QueueUserWorkItem(_ => InvokeSafely(_onClose));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void InvokeSafely(Action action)
    {
        try { action(); }
        catch (Exception) { /* A stale X11 event must not terminate the player. */ }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (Thread.CurrentThread != _events) _events.Join();
        XDestroyWindow(_display, _window);
        XCloseDisplay(_display);
    }

    [DllImport("libX11.so.6")] private static extern int XInitThreads();
    [DllImport("libX11.so.6")] private static extern nint XOpenDisplay(nint displayName);
    [DllImport("libX11.so.6")] private static extern nint XDefaultRootWindow(nint display);
    [DllImport("libX11.so.6")] private static extern nint XCreateSimpleWindow(nint display, nint parent, int x, int y, uint width, uint height, uint borderWidth, nint border, nint background);
    [DllImport("libX11.so.6")] private static extern int XStoreName(nint display, nint window, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport("libX11.so.6")] private static extern int XSelectInput(nint display, nint window, long eventMask);
    [DllImport("libX11.so.6")] private static extern nint XInternAtom(nint display, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);
    [DllImport("libX11.so.6")] private static extern int XSetWMProtocols(nint display, nint window, ref nint protocols, int count);
    [DllImport("libX11.so.6")] private static extern int XMapRaised(nint display, nint window);
    [DllImport("libX11.so.6")] private static extern int XFlush(nint display);
    [DllImport("libX11.so.6")] private static extern int XPending(nint display);
    [DllImport("libX11.so.6")] private static extern int XNextEvent(nint display, nint eventBuffer);
    [DllImport("libX11.so.6")] private static extern nint XLookupKeysym(nint keyEvent, int index);
    [DllImport("libX11.so.6")] private static extern int XDestroyWindow(nint display, nint window);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(nint display);
}
#endif
