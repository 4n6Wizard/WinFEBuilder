using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Reports;

/// <summary>Aggregated data for a build report, rendered to HTML.</summary>
public sealed class ReportModel
{
    public string ApplicationVersion { get; set; } = "1.0.0";
    public DateTimeOffset GeneratedLocal { get; set; } = DateTimeOffset.Now;

    // Environment
    public string? OperatorName { get; set; }
    public string? OrganizationName { get; set; }
    public string? ComputerName { get; set; }
    public string? WindowsVersion { get; set; }
    public string? DotNetVersion { get; set; }
    public string? PowerShellVersion { get; set; }
    public string? AdkVersion { get; set; }
    public string? WinPeArchitectures { get; set; }
    public string? DismPath { get; set; }

    // Build (from build-manifest.json)
    public BuildManifest? Build { get; set; }

    // USB (from usb-record json), optional
    public UsbRecord? Usb { get; set; }

    // Manual validation, optional
    public ValidationRecord? Validation { get; set; }

    /// <summary>
    /// Operational status summary. These reflect ONLY what has been explicitly recorded — nothing
    /// here is auto-marked as passed.
    /// </summary>
    public string BuildStatus => Build?.BuildStatus ?? "Not built";
    public string BootStructureStatus => (Build?.BootStructureValidated ?? false) ? "Validated" : "Not validated";
    public string BootTestStatus => Validation?.BootVerified == true ? "Passed (recorded)" : "NOT TESTED";
    public string WriteProtectionTestStatus => Validation?.WriteProtectionVerified == true ? "Passed (recorded)" : "NOT TESTED";
}
