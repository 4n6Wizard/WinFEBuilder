namespace WinFEBuilder.Core.Models;

/// <summary>Full identity + state of a physical disk, used for safe USB targeting.</summary>
public sealed class DiskInfo
{
    public int Number { get; init; }
    public string? FriendlyName { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public string? UniqueId { get; init; }
    public string? BusType { get; init; }

    public long SizeBytes { get; init; }
    public int PartitionCount { get; init; }

    public List<string> DriveLetters { get; init; } = new();
    public List<string> FileSystems { get; init; } = new();

    public bool IsOffline { get; init; }
    public bool IsReadOnly { get; init; }
    public string? HealthStatus { get; init; }

    public bool IsRemovable { get; init; }
    public bool IsSystemDisk { get; init; }
    public bool IsBootDisk { get; init; }

    /// <summary>True for fake disks used by simulation mode (never touched).</summary>
    public bool IsSimulated { get; init; }

    /// <summary>
    /// True when this disk's partitions/volumes were enumerated successfully, so its DriveLetters are
    /// trustworthy for protected-volume checks. Set false when the WMI partition query fails; a real
    /// disk with this false is refused as a target (we can't prove it hosts nothing protected).
    /// Defaults true so it never spuriously blocks constructed/test disks.
    /// </summary>
    public bool PartitionInfoReliable { get; init; } = true;

    public string CapacityText => SizeBytes <= 0 ? "?" : $"{SizeBytes / 1024d / 1024d / 1024d:0.0} GB";

    /// <summary>
    /// Stable identity signature compared before any destructive action. If ANY of these change
    /// between selection and execution, the operation is aborted.
    /// </summary>
    public string IdentitySignature =>
        string.Join("|",
            Number,
            Model ?? "",
            SerialNumber ?? "",
            UniqueId ?? "",
            SizeBytes,
            BusType ?? "");

    public string Describe() =>
        $"Disk {Number}: {FriendlyName ?? Model ?? "Unknown"} " +
        $"({CapacityText}, {BusType}, SN {SerialNumber ?? "n/a"})" +
        (IsSimulated ? " [SIMULATED]" : "");
}
