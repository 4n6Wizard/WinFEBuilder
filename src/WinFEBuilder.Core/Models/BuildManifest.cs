namespace WinFEBuilder.Core.Models;

/// <summary>Auditable manifest written after a build, capturing commands, hashes, and results.</summary>
public sealed class BuildManifest
{
    public string ManifestVersion { get; init; } = "1.0";
    public string ApplicationVersion { get; init; } = "1.0.0";

    public DateTimeOffset BuildDateLocal { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset BuildDateUtc { get; init; } = DateTimeOffset.UtcNow;

    public string? ComputerName { get; init; }
    public string? Operator { get; init; }
    public string? Organization { get; init; }

    public string? FrameworkSource { get; init; }
    public string? WorkspacePath { get; init; }
    public string? FrameworkInWorkspace { get; init; }

    public string? MediaScript { get; init; }
    public string? IsoScript { get; init; }

    public int? MediaBuildExitCode { get; init; }
    public int? IsoBuildExitCode { get; init; }

    public string? MediaRoot { get; init; }
    public bool BootStructureValidated { get; init; }
    public Dictionary<string, bool> ExpectedBootComponents { get; init; } = new();

    public string? BootWimPath { get; init; }
    public long BootWimSize { get; init; }
    public string? BootWimSha256 { get; init; }
    public string? BootWimArchitecture { get; init; }
    public int BootWimImageCount { get; init; }
    public List<WimImage> BootWimImages { get; init; } = new();

    public string? IsoSourcePath { get; init; }
    public string? IsoDestinationPath { get; init; }
    public long IsoSize { get; init; }
    public string? IsoSha256 { get; init; }

    public List<string> Warnings { get; init; } = new();
    public List<string> Errors { get; init; } = new();

    // Operational validation states — intentionally NOT auto-marked as passed.
    public string BuildStatus { get; init; } = "Build Successful";
    public string BootStructureStatus { get; init; } = "Not Validated";
    public string BootTestStatus { get; init; } = "NOT TESTED";
    public string WriteProtectionTestStatus { get; init; } = "NOT TESTED";
    public string OrganizationApprovalStatus { get; init; } = "NOT APPROVED";
}
