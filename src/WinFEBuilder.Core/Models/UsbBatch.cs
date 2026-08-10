using System.Text;

namespace WinFEBuilder.Core.Models;

/// <summary>Outcome of one target disk within a batch.</summary>
public enum UsbTargetStatus
{
    NotStarted = 0,
    Running = 1,
    Success = 2,
    Failed = 3,
    Canceled = 4,
    Simulated = 5
}

/// <summary>Per-disk result within a USB batch.</summary>
public sealed class UsbTargetResult
{
    public required int DiskNumber { get; init; }
    public required string Describe { get; init; }
    public string? SerialNumber { get; init; }

    public UsbTargetStatus Status { get; set; } = UsbTargetStatus.NotStarted;
    public string? FailedStage { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>The detailed single-USB result (verification, hashes, statuses), when the disk was run.</summary>
    public UsbCreationResult? Result { get; set; }

    public string StatusLine => Status switch
    {
        UsbTargetStatus.Success => "Success",
        UsbTargetStatus.Simulated => "Simulated (no write)",
        UsbTargetStatus.Failed => "Failed: " + (ErrorMessage ?? "unknown error"),
        UsbTargetStatus.Canceled => "Canceled",
        UsbTargetStatus.NotStarted => "Not started",
        UsbTargetStatus.Running => "Running…",
        _ => Status.ToString()
    };
}

/// <summary>Inputs for a sequential multi-USB batch. Safety gates are re-checked inside the service.</summary>
public sealed class UsbBatchRequest
{
    public required IReadOnlyList<DiskInfo> Targets { get; init; }
    public required string MediaSourcePath { get; init; }
    public string Label { get; init; } = "WINFE";
    public string? ConfirmationPhrase { get; init; }
    public bool AcknowledgedDataLoss { get; init; }

    /// <summary>
    /// Explicit operator intent to allow fixed (non-removable) disks as targets. Defaults to false;
    /// protected system/boot/workspace/output disks are refused regardless of this flag.
    /// </summary>
    public bool AllowFixedDisk { get; init; }
}

/// <summary>Progress update for the batch panel (one report per disk transition).</summary>
public sealed class UsbBatchProgress
{
    public required int Current { get; init; }
    public required int Total { get; init; }
    public DiskInfo? Disk { get; init; }
    public required string Stage { get; init; }
    public int Successful { get; init; }
    public int Failed { get; init; }
}

/// <summary>Aggregate result of a sequential USB batch.</summary>
public sealed class UsbBatchResult
{
    public bool Simulated { get; set; }
    public bool GlobalAbort { get; set; }
    public string? GlobalError { get; set; }
    public List<UsbTargetResult> Targets { get; } = new();

    public int Successful => Targets.Count(t => t.Status is UsbTargetStatus.Success or UsbTargetStatus.Simulated);
    public int Failed => Targets.Count(t => t.Status == UsbTargetStatus.Failed);
    public int Canceled => Targets.Count(t => t.Status is UsbTargetStatus.Canceled or UsbTargetStatus.NotStarted);

    public string SummaryText()
    {
        var sb = new StringBuilder();
        if (GlobalAbort)
        {
            sb.AppendLine("Batch could not start");
            sb.AppendLine();
            sb.AppendLine(GlobalError ?? "Unknown error.");
            return sb.ToString();
        }

        sb.AppendLine(Simulated ? "Batch complete (SIMULATION — no disks modified)" : "Batch complete");
        sb.AppendLine();
        sb.AppendLine($"Successful: {Successful}");
        sb.AppendLine($"Failed: {Failed}");
        sb.AppendLine($"Canceled: {Canceled}");
        sb.AppendLine();
        foreach (var t in Targets)
            sb.AppendLine($"Disk {t.DiskNumber} — {t.StatusLine}");
        return sb.ToString();
    }
}
