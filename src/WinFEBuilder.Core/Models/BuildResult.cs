using WinFEBuilder.Core.Services;

namespace WinFEBuilder.Core.Models;

/// <summary>A single stage in the build workflow, surfaced to the UI as a status row.</summary>
public sealed class BuildStage
{
    public required string Name { get; init; }
    public CheckStatus Status { get; set; } = CheckStatus.NotConfigured;
    public string Detail { get; set; } = string.Empty;
}

/// <summary>Aggregate result of the end-to-end Build workflow.</summary>
public sealed class BuildResult
{
    public bool Success { get; set; }

    /// <summary>
    /// Highest operational state reached. The Build workflow may set BuildSuccessful and
    /// BootStructureValidated only. Boot / write-protection tests remain NotTested (manual).
    /// </summary>
    public ValidationStatus OperationalStatus { get; set; } = ValidationStatus.NotTested;

    public string? WorkspacePath { get; set; }
    public string? FrameworkInWorkspace { get; set; }

    public string? MediaScript { get; set; }
    public string? IsoScript { get; set; }

    public ProcessRunResult? MediaBuildRun { get; set; }
    public ProcessRunResult? IsoBuildRun { get; set; }

    public MediaValidationResult? Media { get; set; }
    public IsoValidationResult? Iso { get; set; }

    public string? ManifestPath { get; set; }

    public List<BuildStage> Stages { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Errors { get; } = new();

    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset FinishTime { get; set; }
    public TimeSpan Duration => FinishTime - StartTime;

    public string? RecommendedAction { get; set; }

    public BuildStage AddStage(string name)
    {
        var s = new BuildStage { Name = name };
        Stages.Add(s);
        return s;
    }

    // Status strings the UI shows, keeping build success separate from forensic validation.
    public string BootTestStatus => "NOT TESTED";
    public string WriteProtectionTestStatus => "NOT TESTED";
}
