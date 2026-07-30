namespace Elyra.Services;

public interface ISystemMediaTransportService
{
    void Initialize(Window window);
}

public sealed class NoOpSystemMediaTransportService : ISystemMediaTransportService
{
    public void Initialize(Window window) { }
}
