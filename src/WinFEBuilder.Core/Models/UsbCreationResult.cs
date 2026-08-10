namespace WinFEBuilder.Core.Models;

/// <summary>Canonical names + statuses for the mandatory per-USB pipeline stages.</summary>
public static class UsbStage
{
    public const string Revalidation = "Target revalidation";
    public const string DiskPart = "DiskPart preparation";
    public const string DriveLetter = "Drive-letter detection";
    public const string MediaCopy = "Media copy";
    public const string BootConfig = "Boot configuration";
    public const string Verification = "File verification";

    public const string Pending = "PENDING";
    public const string Pass = "PASS";
    public const string Fail = "FAIL";
    public const string Skipped = "SKIPPED";

    /// <summary>Ordered list of the stages every real write MUST pass to be a success.</summary>
    public static readonly IReadOnlyList<string> Mandatory = new[]
    {
        Revalidation, DiskPart, DriveLetter, MediaCopy, BootConfig, Verification
    };
}

/// <summary>Status of one pipeline stage.</summary>
public sealed class StageResult
{
    public required string Name { get; init; }
    public string Status { get; set; } = UsbStage.Pending;
    public string? Detail { get; set; }
}

/// <summary>Result of a USB creation attempt (or simulation).</summary>
public sealed class UsbCreationResult
{
    public bool Simulated { get; set; }
    public bool Executed { get; set; }

    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>The exact DiskPart script that was (or would be) run.</summary>
    public string DiskPartScript { get; set; } = string.Empty;

    // ---- DiskPart execution detail ----
    public string? DiskPartCommand { get; set; }
    public int? DiskPartExitCode { get; set; }
    public string? DiskPartOutput { get; set; }
    public bool DiskPartTimedOut { get; set; }
    public int? DiskPartTimeoutSeconds { get; set; }
    public double? DiskPartElapsedSeconds { get; set; }

    // ---- Boot configuration (bootsect) execution detail ----
    public string? BootSectExecutable { get; set; }
    public string? BootSectArguments { get; set; }
    public int? BootSectExitCode { get; set; }
    public string? BootSectError { get; set; }
    public bool BootSectTimedOut { get; set; }

    public string? AssignedDriveLetter { get; set; }
    public int FilesCopied { get; set; }
    public long BytesCopied { get; set; }

    /// <summary>Files that verification required but did not find on the finished volume.</summary>
    public List<string> MissingFiles { get; } = new();

    public List<FileHashEntry> CriticalHashes { get; } = new();

    /// <summary>Per-stage status for the mandatory pipeline stages.</summary>
    public List<StageResult> Stages { get; } = new();

    // Status lines shown to the user — build/structure vs. manual forensic tests.
    public string UsbCreationStatus { get; set; } = "FAIL";
    public string BootStructureStatus { get; set; } = "FAIL";
    public string OfflineStructuralValidationStatus { get; set; } = "FAIL";

    public List<string> Warnings { get; } = new();
    public List<string> Errors { get; } = new();
    public string? RecommendedAction { get; set; }

    /// <summary>The workflow stage that failed (e.g. "Boot configuration"), when applicable.</summary>
    public string? FailedStage { get; set; }

    /// <summary>Record/replace the status of a named stage.</summary>
    public void SetStage(string name, string status, string? detail = null)
    {
        var existing = Stages.FirstOrDefault(s => s.Name == name);
        if (existing is null)
            Stages.Add(new StageResult { Name = name, Status = status, Detail = detail });
        else
        {
            existing.Status = status;
            if (detail is not null) existing.Detail = detail;
        }
    }

    public string StageStatus(string name)
        => Stages.FirstOrDefault(s => s.Name == name)?.Status ?? UsbStage.Pending;

    /// <summary>True only when EVERY mandatory stage recorded a PASS. Verification alone can never
    /// flip a result to success — a prior failed stage keeps this false.</summary>
    public bool AllMandatoryStagesPassed()
        => UsbStage.Mandatory.All(m => StageStatus(m) == UsbStage.Pass);

    /// <summary>
    /// A real write is a success ONLY when it executed, produced no errors, AND every mandatory stage
    /// passed. File copy alone is never sufficient.
    /// </summary>
    public bool Success => Executed && Errors.Count == 0 && AllMandatoryStagesPassed();
}
