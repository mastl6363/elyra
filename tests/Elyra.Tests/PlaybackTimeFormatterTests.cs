using Elyra.Services;

namespace Elyra.Tests;

public sealed class PlaybackTimeFormatterTests
{
    [Fact]
    public void FormatElapsed_UsesTrackTimeFormat()
    {
        Assert.Equal("1:05", PlaybackTimeFormatter.FormatElapsed(TimeSpan.FromSeconds(65)));
        Assert.Equal("1:02:03", PlaybackTimeFormatter.FormatElapsed(new TimeSpan(1, 2, 3)));
    }

    [Fact]
    public void FormatRemaining_ShowsCountdownAndClampsAtZero()
    {
        Assert.Equal("-1:45", PlaybackTimeFormatter.FormatRemaining(
            TimeSpan.FromMinutes(1.25),
            TimeSpan.FromMinutes(3)));
        Assert.Equal("-0:00", PlaybackTimeFormatter.FormatRemaining(
            TimeSpan.FromMinutes(4),
            TimeSpan.FromMinutes(3)));
    }

    [Fact]
    public void FormatRemaining_ShowsPlaceholderWhenDurationIsUnknown()
    {
        Assert.Equal("--:--", PlaybackTimeFormatter.FormatRemaining(TimeSpan.Zero, TimeSpan.Zero));
    }
}
