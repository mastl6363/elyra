namespace Elyra.Models;

public sealed class PlaybackPreferences
{
    public bool GaplessEnabled { get; set; } = true;
    public int CrossfadeSeconds { get; set; }
    public bool NormalizeVolume { get; set; }
    public EqualizerPreferences Equalizer { get; set; } = new();
}
