namespace WinFEBuilder.Core.Configuration;

/// <summary>
/// A reusable build profile. Note: disk numbers are intentionally NEVER stored here
/// (they are unstable and unsafe to persist).
/// </summary>
public sealed class BuildProfile
{
    public string Name { get; set; } = "Default";
    public string? FrameworkPath { get; set; }
    public string? WorkspaceRoot { get; set; }
    public string? OutputRoot { get; set; }
    public List<string> SelectedTools { get; set; } = new();
    public List<string> SelectedDrivers { get; set; } = new();
    public string? Wallpaper { get; set; }
    public string? OrganizationName { get; set; }
    public string? OperatorDefault { get; set; }

    /// <summary>"UEFI", "Legacy", or "Both".</summary>
    public string UsbLayout { get; set; } = "Both";

    public Dictionary<string, string> BuildOptions { get; set; } = new();
}
