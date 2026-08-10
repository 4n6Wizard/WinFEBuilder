using WinFEBuilder.Core.Services;

namespace WinFEBuilder.Core.Models;

/// <summary>Details of a detected Windows ADK + WinPE add-on installation.</summary>
public sealed class AdkInstallation
{
    public bool Found { get; set; }

    /// <summary>e.g. "10.1.26100.1" — not hardcoded, discovered from the install.</summary>
    public string? Version { get; set; }

    /// <summary>
    /// Every ADK version discovered on the machine (side-by-side kits are common), newest first.
    /// </summary>
    public List<string> DetectedVersions { get; } = new();

    /// <summary>
    /// Whether the installed kit(s) satisfy the WinFE ADK 1809 requirement — see
    /// <see cref="AdkVersionPolicy"/>. <see cref="AdkVersionSupport.Unknown"/> when no version could
    /// be determined.
    /// </summary>
    public AdkVersionSupport VersionSupport { get; set; } = AdkVersionSupport.Unknown;

    /// <summary>True when a compatible kit is installed alongside an incompatible one.</summary>
    public bool HasMixedVersionInstalls { get; set; }

    /// <summary>True when the detected version(s) are known to be incompatible with WinFE.</summary>
    public bool IsUnsupportedVersion => VersionSupport == AdkVersionSupport.Unsupported;

    public string? AdkRoot { get; set; }
    public string? WinPeRoot { get; set; }
    public string? DismPath { get; set; }
    public string? OscdimgPath { get; set; }

    /// <summary>Deployment and Imaging Tools Environment (DandISetEnv.bat).</summary>
    public string? DandISetEnvPath { get; set; }

    /// <summary>Path to WinPE optional components (WinPE_OCs) if present.</summary>
    public string? WinPeOptionalComponentsPath { get; set; }

    /// <summary>Path to WinPE base media (Media folder) if present.</summary>
    public string? WinPeMediaPath { get; set; }

    public List<string> SupportedArchitectures { get; } = new();

    public List<string> Warnings { get; } = new();

    public bool WinPeAddOnPresent =>
        !string.IsNullOrEmpty(WinPeRoot) &&
        (!string.IsNullOrEmpty(WinPeOptionalComponentsPath) || !string.IsNullOrEmpty(WinPeMediaPath));
}
