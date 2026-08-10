namespace WinFEBuilder.Core.Models;

/// <summary>Inputs for USB creation. All safety gates are re-checked inside the service.</summary>
public sealed class UsbBuildRequest
{
    /// <summary>The disk selected in the UI (its identity is re-verified before any write).</summary>
    public required DiskInfo SelectedDisk { get; init; }

    /// <summary>Media source root (the folder containing Boot\ EFI\ Sources\boot.wim).</summary>
    public required string MediaSourcePath { get; init; }

    /// <summary>FAT32 volume label.</summary>
    public string Label { get; init; } = "WINFE";

    /// <summary>The exact typed confirmation phrase, e.g. "ERASE DISK 3".</summary>
    public string? ConfirmationPhrase { get; init; }

    /// <summary>The "I understand…" acknowledgement checkbox.</summary>
    public bool AcknowledgedDataLoss { get; init; }

    /// <summary>
    /// Explicit operator intent to allow a fixed (non-removable) disk as a target. Defaults to false,
    /// so the service refuses internal/fixed disks unless the operator deliberately opted in. Protected
    /// system/boot/workspace/output disks are refused regardless of this flag.
    /// </summary>
    public bool AllowFixedDisk { get; init; }
}
