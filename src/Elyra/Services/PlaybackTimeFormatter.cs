namespace Elyra.Services;

public static class PlaybackTimeFormatter
{
    public static string FormatElapsed(TimeSpan position) =>
        Format(position < TimeSpan.Zero ? TimeSpan.Zero : position);

    public static string FormatRemaining(TimeSpan position, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return "--:--";

        var remaining = duration - position;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        return $"-{Format(remaining)}";
    }

    private static string Format(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss")
        : value.ToString(@"m\:ss");
}
