using Elyra.Services;

namespace Elyra;

public partial class App : Application
{
    private readonly ISystemMediaTransportService _mediaTransport;

    public App(ISystemMediaTransportService mediaTransport)
    {
        _mediaTransport = mediaTransport;
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage()) { Title = "Elyra" };
        window.HandlerChanged += (_, _) => _mediaTransport.Initialize(window);
        return window;
    }
}
