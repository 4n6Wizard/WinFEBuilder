using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Validation;

/// <summary>
/// Pure, IO-free rules that decide whether a disk may be a USB target. These are the core safety
/// gate: any protected/ambiguous disk is refused with an explicit reason. Unit tested extensively.
/// </summary>
public static class DiskEligibilityRules
{
    /// <summary>
    /// Evaluate a disk. A disk is eligible ONLY if none of the protection rules trip.
    /// </summary>
    /// <param name="allowNonRemovable">
    /// When false (default), non-removable disks are refused. The advanced UI toggle may set this
    /// true, but protected system/boot disks are STILL always refused.
    /// </param>
    public static DiskEligibility Evaluate(DiskInfo disk, ProtectedContext ctx, bool allowNonRemovable = false)
    {
        var reasons = new List<string>();

        if (disk is null)
            return new DiskEligibility { CanTarget = false, BlockReasons = new[] { "No disk." } };

        if (disk.IsSimulated)
        {
            // Simulated disks are "eligible" only to demonstrate the flow; execution never happens.
            return new DiskEligibility { DiskNumber = disk.Number, CanTarget = true, BlockReasons = Array.Empty<string>() };
        }

        // Fail closed: if the disk's partitions/volumes could not be enumerated, its DriveLetters are
        // untrustworthy, so we cannot prove it hosts no protected volume. Refuse rather than guess.
        if (!disk.PartitionInfoReliable)
            reasons.Add("This disk's volumes could not be verified (partition enumeration failed) — refused for safety.");

        if (disk.IsSystemDisk)
            reasons.Add("This is the Windows system disk.");
        if (disk.IsBootDisk)
            reasons.Add("This is the boot disk.");

        // Hosts a protected volume (system/windows/pagefile/workspace/framework/ISO/output).
        foreach (var letter in disk.DriveLetters)
        {
            var norm = ProtectedContext.Normalize(letter);
            if (norm is not null && ctx.ProtectedDriveLetters.Contains(norm))
            {
                var reason = ctx.ReasonByDriveLetter.TryGetValue(norm, out var r) ? r : "a protected location";
                reasons.Add($"Hosts {norm} ({reason}).");
            }
        }

        if (string.IsNullOrWhiteSpace(disk.UniqueId))
            reasons.Add("Disk is not uniquely identifiable (no UniqueId).");

        if (disk.SizeBytes <= 0)
            reasons.Add("Disk reports an invalid or zero size.");

        if (disk.IsReadOnly)
            reasons.Add("Disk is read-only.");

        if (!disk.IsRemovable && !allowNonRemovable)
            reasons.Add("Disk is not removable (enable the advanced toggle to target fixed disks).");

        return new DiskEligibility
        {
            DiskNumber = disk.Number,
            CanTarget = reasons.Count == 0,
            BlockReasons = reasons
        };
    }
}
