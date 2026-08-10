namespace WinFEBuilder.Core.Models;

/// <summary>Persisted record of a USB creation (success OR failure), used by reports.</summary>
public sealed class UsbRecord
{
    public DateTimeOffset CreatedLocal { get; init; } = DateTimeOffset.Now;

    // Disk identity (captured at write time)
    public int DiskNumber { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public string? UniqueId { get; init; }
    public string? BusType { get; init; }
    public long CapacityBytes { get; init; }
    public string? AssignedDriveLetter { get; init; }

    public string? MediaSourcePath { get; init; }
    public string Label { get; init; } = "WINFE";

    // Timing
    public DateTimeOffset? StartTime { get; init; }
    public DateTimeOffset? EndTime { get; init; }

    public int FilesCopied { get; init; }
    public long BytesCopied { get; init; }

    // Per-stage status (every mandatory stage)
    public List<StageResult> Stages { get; init; } = new();

    // DiskPart execution detail
    public string? DiskPartCommand { get; init; }
    public int? DiskPartExitCode { get; init; }
    public bool DiskPartTimedOut { get; init; }

    // Boot configuration (bootsect) execution detail
    public string? BootSectCommand { get; init; }
    public int? BootSectExitCode { get; init; }
    public bool BootSectTimedOut { get; init; }
    public string? BootSectError { get; init; }

    // Verification
    public List<string> MissingFiles { get; init; } = new();

    public string? FailedStage { get; init; }
    public string? ErrorMessage { get; init; }

    public string UsbCreationStatus { get; init; } = "FAIL";
    public string BootStructureStatus { get; init; } = "FAIL";
    public string OfflineStructuralValidationStatus { get; init; } = "FAIL";

    /// <summary>The single final outcome for this disk: SUCCESS / FAILED / SIMULATED.</summary>
    public string FinalStatus { get; init; } = "FAILED";

    public List<FileHashEntry> CriticalHashes { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}
