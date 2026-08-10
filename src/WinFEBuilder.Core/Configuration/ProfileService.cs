using System.Text.Json;

namespace WinFEBuilder.Core.Configuration;

/// <summary>
/// Loads/saves reusable build profiles. Disk numbers are never stored (they are unstable/unsafe).
/// Seeds a standard set of profiles if none exist.
/// </summary>
public sealed class ProfileService : IProfileService
{
    private sealed class ProfilesFile
    {
        public List<BuildProfile> Profiles { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string ProfilesFilePath { get; }

    public ProfileService(string profilesFilePath)
    {
        ProfilesFilePath = profilesFilePath ?? throw new ArgumentNullException(nameof(profilesFilePath));
        EnsureDefaults();
    }

    public IReadOnlyList<BuildProfile> List() => Read().Profiles;

    public BuildProfile? Get(string name) =>
        Read().Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public void Save(BuildProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new ArgumentException("Profile name is required.", nameof(profile));

        var file = Read();
        file.Profiles.RemoveAll(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        file.Profiles.Add(profile);
        Write(file);
    }

    public void Delete(string name)
    {
        var file = Read();
        file.Profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        Write(file);
    }

    private ProfilesFile Read()
    {
        try
        {
            if (File.Exists(ProfilesFilePath))
            {
                var file = JsonSerializer.Deserialize<ProfilesFile>(File.ReadAllText(ProfilesFilePath), JsonOptions) ?? new ProfilesFile();
                foreach (var p in file.Profiles) Heal(p);
                return file;
            }
        }
        catch { /* fall through to empty */ }
        return new ProfilesFile();
    }

    // Drop absolute path fields that don't exist on this machine (e.g. C:\... values carried over from
    // another computer when a profiles file is shared). Relative and existing paths are kept.
    private static void Heal(BuildProfile p)
    {
        p.FrameworkPath = HealPath(p.FrameworkPath);
        p.WorkspaceRoot = HealPath(p.WorkspaceRoot);
        p.OutputRoot = HealPath(p.OutputRoot);
        p.Wallpaper = HealPath(p.Wallpaper);
    }

    private static string? HealPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return path;
        return (File.Exists(path) || Directory.Exists(path)) ? path : null;
    }

    private void Write(ProfilesFile file)
    {
        var dir = Path.GetDirectoryName(ProfilesFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(ProfilesFilePath, JsonSerializer.Serialize(file, JsonOptions));
    }

    private void EnsureDefaults()
    {
        var file = Read();
        if (file.Profiles.Count > 0) return;

        file.Profiles.AddRange(new[]
        {
            new BuildProfile { Name = "Agency Standard", UsbLayout = "Both" },
            new BuildProfile { Name = "Lab", UsbLayout = "Both" },
            new BuildProfile { Name = "Field Response", UsbLayout = "UEFI" },
            new BuildProfile { Name = "Training", UsbLayout = "Both" },
            new BuildProfile { Name = "Legacy BIOS", UsbLayout = "Legacy" },
            new BuildProfile { Name = "UEFI Only", UsbLayout = "UEFI" },
        });
        Write(file);
    }
}
