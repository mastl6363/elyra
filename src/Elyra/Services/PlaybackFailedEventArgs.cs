namespace Elyra.Services;

public sealed class PlaybackFailedEventArgs(string source) : EventArgs
{
    public string Source { get; } = source;
}
