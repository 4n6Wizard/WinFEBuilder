using System.Text.Json.Serialization;

namespace WinFEBuilder.Core.Configuration;

/// <summary>Root application settings, loaded from config/settings.json.</summary>
public sealed class AppSettings
{
    // Relative by default so data folders live beside the executable (portable). An absolute path
    // here (e.g. "D:\\Cases\\workspace") overrides that and is used as-is. Resolution happens in
    // CoreServiceRegistration against AppPaths.RootDir.
    public string WorkspaceRoot { get; set; } = "workspace";
    public string OutputRoot { get; set; } = "output";
    public string LogRoot { get; set; } = "logs";
    public string ReportRoot { get; set; } = "reports";

    /// <summary>Last selected framework directory (for convenience only).</summary>
    public string? LastFrameworkPath { get; set; }

    public string? OperatorName { get; set; }
    public string? OrganizationName { get; set; }

    /// <summary>Minimum free space (GB) required on the workspace volume for a build.</summary>
    public double MinimumFreeSpaceGb { get; set; } = 15.0;

    /// <summary>
    /// Developer-only guard: when on, destructive USB commands are generated and displayed but never
    /// executed.
    /// <para>
    /// <b>Not an operator setting.</b> It is <see cref="JsonIgnoreAttribute">excluded from
    /// settings.json</see> so it never appears in an operator's config, and it is always
    /// <c>false</c> in Release builds — the released tool always writes for real. DEBUG builds force
    /// it on in <c>SettingsService</c> so running from an IDE can never erase a disk.
    /// </para>
    /// It was previously an operator-facing option defaulting to <c>true</c>, which made the released
    /// tool look broken: nothing happened and a fake disk #99 appeared. Real protection comes from the
    /// protected-disk rules, the typed <c>ERASE DISK &lt;n&gt;</c> confirmation, and the pre-write
    /// identity re-verification — not from this flag.
    /// </summary>
    [JsonIgnore]
    public bool SimulationMode { get; set; } = false;

    /// <summary>Preferred PowerShell executable: "powershell" (5.1) or "pwsh" (7+).</summary>
    public string PreferredPowerShell { get; set; } = "powershell";

    /// <summary>
    /// Maximum time (seconds) to allow a single DiskPart clean/format to run before it is treated as a
    /// timeout (the process is terminated and only that target fails). Slow/large USB media routinely
    /// exceed the old five-minute default, so this defaults to 15 minutes.
    /// </summary>
    public int DiskPartTimeoutSeconds { get; set; } = 900;
}
