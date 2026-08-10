namespace WinFEBuilder.Core.Configuration;

public interface ISettingsService
{
    AppSettings Settings { get; }
    string SettingsFilePath { get; }

    AppSettings Load();
    void Save();
    void Save(AppSettings settings);
}
