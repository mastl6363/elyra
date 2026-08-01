using Elyra.Models;
using LibVLCSharp.Shared;

namespace Elyra.Services;

/// <summary>Owns the persistent graphic equalizer and applies it to every LibVLC player.</summary>
public sealed class EqualizerService
{
    private readonly PlaybackPreferencesService _preferences;
    private readonly List<MediaPlayer> _players = [];
    private EqualizerPreferences _settings;

    public EqualizerService(PlaybackPreferencesService preferences)
    {
        _preferences = preferences;
        using var probe = new Equalizer();
        Frequencies = Enumerable.Range(0, checked((int)probe.BandCount))
            .Select(index => probe.BandFrequency((uint)index))
            .ToList();
        Presets = Enumerable.Range(0, checked((int)probe.PresetCount))
            .Select(CreatePreset)
            .ToList();
        _settings = Normalize(preferences.Equalizer);
    }

    public IReadOnlyList<float> Frequencies { get; }
    public IReadOnlyList<EqualizerPresetDefinition> Presets { get; }
    public bool Enabled => _settings.Enabled;
    public int PresetIndex => _settings.PresetIndex;
    public float Preamp => _settings.Preamp;
    public IReadOnlyList<float> Bands => _settings.Bands;
    public event EventHandler? Changed;

    public void AttachPlayers(IEnumerable<MediaPlayer> players)
    {
        _players.Clear();
        _players.AddRange(players);
        Apply();
    }

    public void DetachPlayers() => _players.Clear();

    public void SetEnabled(bool enabled)
    {
        if (_settings.Enabled == enabled)
            return;
        _settings.Enabled = enabled;
        SaveAndApply();
    }

    public void SelectPreset(int index)
    {
        var preset = Presets.FirstOrDefault(item => item.Index == index);
        if (preset is null)
            return;
        _settings.PresetIndex = preset.Index;
        _settings.Preamp = preset.Preamp;
        _settings.Bands = [.. preset.Bands];
        SaveAndApply();
    }

    public void SetPreamp(float value)
    {
        _settings.Preamp = Math.Clamp(value, -20, 20);
        _settings.PresetIndex = -1;
        SaveAndApply();
    }

    public void SetBand(int index, float value)
    {
        if (index < 0 || index >= _settings.Bands.Count)
            return;
        _settings.Bands[index] = Math.Clamp(value, -20, 20);
        _settings.PresetIndex = -1;
        SaveAndApply();
    }

    private EqualizerPresetDefinition CreatePreset(int index)
    {
        using var equalizer = new Equalizer((uint)index);
        return new EqualizerPresetDefinition(
            index,
            equalizer.PresetName((uint)index) ?? $"Preset {index + 1}",
            equalizer.Preamp,
            Enumerable.Range(0, Frequencies.Count)
                .Select(band => equalizer.Amp((uint)band))
                .ToList());
    }

    private EqualizerPreferences Normalize(EqualizerPreferences source)
    {
        var presetIndex = Presets.Any(preset => preset.Index == source.PresetIndex)
            ? source.PresetIndex
            : Presets.FirstOrDefault()?.Index ?? -1;
        var preset = Presets.FirstOrDefault(item => item.Index == presetIndex);
        var bands = source.Bands.Count == Frequencies.Count
            ? source.Bands.Select(value => Math.Clamp(value, -20, 20)).ToList()
            : preset?.Bands.ToList() ?? Enumerable.Repeat(0f, Frequencies.Count).ToList();

        return new EqualizerPreferences
        {
            Enabled = source.Enabled,
            PresetIndex = presetIndex,
            Preamp = source.Bands.Count == Frequencies.Count
                ? Math.Clamp(source.Preamp, -20, 20)
                : preset?.Preamp ?? 0,
            Bands = bands
        };
    }

    private void SaveAndApply()
    {
        _preferences.UpdateEqualizer(_settings);
        Apply();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Apply()
    {
        foreach (var player in _players)
        {
            if (!_settings.Enabled)
            {
                player.UnsetEqualizer();
                continue;
            }

            using var equalizer = new Equalizer();
            equalizer.SetPreamp(_settings.Preamp);
            for (var index = 0; index < Math.Min(_settings.Bands.Count, Frequencies.Count); index++)
                equalizer.SetAmp(_settings.Bands[index], (uint)index);
            player.SetEqualizer(equalizer);
        }
    }
}
