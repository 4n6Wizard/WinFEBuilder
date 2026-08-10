namespace WinFEBuilder.Core.Models;

/// <summary>Outcome of adding one driver via DISM.</summary>
public sealed class DriverAddResult
{
    public required string InfName { get; init; }
    public int ExitCode { get; init; }
    public bool Success => ExitCode == 0;
}

/// <summary>Result of a driver-injection session against a copied boot.wim.</summary>
public sealed class DriverInjectionResult
{
    public bool Success { get; set; }
    public string BootWimPath { get; set; } = string.Empty;

    public string? MountDirectory { get; set; }
    public bool ImageMounted { get; set; }
    public bool ImageUnmounted { get; set; }
    public bool Committed { get; set; }

    public string? Sha256Before { get; set; }
    public string? Sha256After { get; set; }

    public List<DriverAddResult> Added { get; } = new();

    /// <summary>Read-only WIM re-validation after injection.</summary>
    public WimInfo? RevalidatedWim { get; set; }

    /// <summary>
    /// Number of the framework's registry settings successfully re-applied after component
    /// installation. WinFE requires write protection to be written last; DISM package servicing
    /// reverts values written earlier.
    /// </summary>
    public int RegistrySettingsReapplied { get; set; }

    /// <summary>
    /// Whether every registry setting replayed cleanly. Null when no replay was requested.
    /// False means the image may have lost write protection and must not be trusted.
    /// </summary>
    public bool? RegistryReapplySucceeded { get; set; }

    public string? DismLogPath { get; set; }
    public List<string> Warnings { get; } = new();
    public List<string> Errors { get; } = new();
    public string? RecommendedAction { get; set; }

    public int DriversAdded => Added.Count(a => a.Success);
    public int DriversFailed => Added.Count(a => !a.Success);
}
