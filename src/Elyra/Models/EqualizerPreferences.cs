namespace Elyra.Models;

public sealed class EqualizerPreferences
{
    public bool Enabled { get; set; }
    public int PresetIndex { get; set; }
    public float Preamp { get; set; }
    public List<float> Bands { get; set; } = [];
}

public sealed record EqualizerPresetDefinition(
    int Index,
    string Name,
    float Preamp,
    IReadOnlyList<float> Bands);
