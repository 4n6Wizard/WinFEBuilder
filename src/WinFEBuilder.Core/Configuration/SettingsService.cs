using System.Text.Json;

namespace WinFEBuilder.Core.Configuration;

/// <summary>Loads/saves <see cref="AppSettings"/> from a JSON file, with sane defaults.</summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string SettingsFilePath { get; }
    public AppSettings Settings { get; private set; } = new();

    public SettingsService(string settingsFilePath)
    {
        SettingsFilePath = settingsFilePath ?? throw new ArgumentNullException(nameof(settingsFilePath));
        Load();
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is not null)
                    Settings = loaded;
            }
        }
        catch
        {
            // Corrupt/unreadable settings fall back to defaults rather than crashing startup.
            Settings = new AppSettings();
        }

        // "Last used" convenience paths can point at another machine when the config is shared/copied.
        // Drop a framework path that does not exist on this computer so it never resurfaces elsewhere.
        if (!string.IsNullOrWhiteSpace(Settings.LastFrameworkPath) && !Directory.Exists(Settings.LastFrameworkPath))
            Settings.LastFrameworkPath = null;

#if DEBUG
        // Developer safety: a DEBUG build never writes to a disk, so running from an IDE cannot erase
        // one. Not operator-configurable — the property is excluded from settings.json, and Release
        // builds always write for real.
        Settings.SimulationMode = true;
#endif
        return Settings;
    }

    public void Save() => Save(Settings);

    public void Save(AppSettings settings)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        var dir = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
