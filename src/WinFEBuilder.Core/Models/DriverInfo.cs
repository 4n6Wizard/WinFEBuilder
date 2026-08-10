namespace WinFEBuilder.Core.Models;

/// <summary>A discovered .inf driver and its detected metadata.</summary>
public sealed class DriverInfo
{
    public required string InfPath { get; init; }
    public required string InfName { get; init; }

    /// <summary>Architectures the .inf declares (e.g. "amd64", "x86", "arm64"); empty if undetermined.</summary>
    public List<string> Architectures { get; init; } = new();

    public string? DriverClass { get; init; }
    public string? Provider { get; init; }

    /// <summary>False when the driver clearly targets an architecture the media does not use.</summary>
    public bool CompatibleWithTarget { get; set; } = true;
    public string? IncompatibilityReason { get; set; }

    /// <summary>
    /// Which Windows builds this .inf's device entries actually apply to, relative to the image being
    /// built. A driver can install cleanly and still never bind — see <see cref="OsSupportWarning"/>.
    /// </summary>
    public Validation.InfOsSupport? OsSupport { get; set; }

    /// <summary>
    /// Set when every device this .inf serves requires a newer Windows than the target image. DISM
    /// reports success and the driver never loads, so the operator only discovers it as missing
    /// hardware on the booted machine — with nothing to distinguish it from a correct driver.
    /// </summary>
    public string? OsSupportWarning { get; set; }

    public bool HasOsSupportWarning => !string.IsNullOrEmpty(OsSupportWarning);

    public bool Selected { get; set; } = true;

    public string ArchitecturesText => Architectures.Count > 0 ? string.Join(", ", Architectures) : "unknown";

    /// <summary>Short compatibility phrase for the driver list.</summary>
    public string OsSupportText => OsSupport?.Summary ?? "not analysed";
}
