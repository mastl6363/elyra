using Elyra.Services;

namespace Elyra.Tests;

public sealed class PlaybackPreferencesServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"elyra-preferences-{Guid.NewGuid():N}");
    private string PreferencesPath => Path.Combine(_directory, "preferences.json");

    [Fact]
    public void Preferences_AreClampedPersistedAndRestored()
    {
        var preferences = new PlaybackPreferencesService(PreferencesPath)
        {
            GaplessEnabled = false,
            CrossfadeSeconds = 99,
            NormalizeVolume = true
        };

        var restored = new PlaybackPreferencesService(PreferencesPath);

        Assert.False(restored.GaplessEnabled);
        Assert.Equal(12, restored.CrossfadeSeconds);
        Assert.True(restored.NormalizeVolume);
        Assert.Equal(12, restored.TransitionSeconds);
    }

    [Fact]
    public void Gapless_UsesShortTransitionOnlyWithoutCrossfade()
    {
        var preferences = new PlaybackPreferencesService(PreferencesPath);

        Assert.Equal(0.12, preferences.TransitionSeconds, 2);

        preferences.CrossfadeSeconds = 5;
        Assert.Equal(5, preferences.TransitionSeconds);

        preferences.CrossfadeSeconds = 0;
        preferences.GaplessEnabled = false;
        Assert.Equal(0, preferences.TransitionSeconds);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }
}
