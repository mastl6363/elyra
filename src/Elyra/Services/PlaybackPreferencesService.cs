using System.Text.Json;
using Elyra.Models;

namespace Elyra.Services;

/// <summary>Persistent user-facing audio transition and loudness preferences.</summary>
public sealed class PlaybackPreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private PlaybackPreferences _preferences = new();

    public PlaybackPreferencesService()
        : this(Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "playback-preferences.json")) { }

    public PlaybackPreferencesService(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public bool GaplessEnabled
    {
        get => _preferences.GaplessEnabled;
        set
        {
            if (_preferences.GaplessEnabled == value) return;
            _preferences.GaplessEnabled = value;
            Persist();
        }
    }

    public int CrossfadeSeconds
    {
        get => _preferences.CrossfadeSeconds;
        set
        {
            var safeValue = Math.Clamp(value, 0, 12);
            if (_preferences.CrossfadeSeconds == safeValue) return;
            _preferences.CrossfadeSeconds = safeValue;
            Persist();
        }
    }

    public bool NormalizeVolume
    {
        get => _preferences.NormalizeVolume;
        set
        {
            if (_preferences.NormalizeVolume == value) return;
            _preferences.NormalizeVolume = value;
            Persist();
        }
    }

    public double TransitionSeconds =>
        CrossfadeSeconds > 0 ? CrossfadeSeconds : GaplessEnabled ? 0.12 : 0;

    public event EventHandler? Changed;

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            _preferences = JsonSerializer.Deserialize<PlaybackPreferences>(File.ReadAllText(_filePath)) ?? new();
            _preferences.CrossfadeSeconds = Math.Clamp(_preferences.CrossfadeSeconds, 0, 12);
        }
        catch
        {
            _preferences = new PlaybackPreferences();
        }
    }

    private void Persist()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_preferences, JsonOptions));
        }
        catch { }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
