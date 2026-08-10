namespace WinFEBuilder.Core.Validation;

/// <summary>
/// Batch-aware destructive-confirmation phrase. For a single disk it matches the existing
/// "ERASE DISK &lt;n&gt;" phrase; for multiple disks it requires "ERASE &lt;count&gt; DISKS".
/// Pure and unit-tested.
/// </summary>
public static class BatchConfirmationValidator
{
    /// <summary>Build the exact required phrase for the selected disk numbers.</summary>
    public static string Expected(IReadOnlyList<int> diskNumbers)
    {
        if (diskNumbers is null || diskNumbers.Count == 0) return "ERASE 0 DISKS";
        return diskNumbers.Count == 1
            ? $"ERASE DISK {diskNumbers[0]}"
            : $"ERASE {diskNumbers.Count} DISKS";
    }

    /// <summary>True only when the typed text exactly matches the expected phrase (case-sensitive).</summary>
    public static bool IsValid(string? typed, IReadOnlyList<int> diskNumbers)
    {
        if (typed is null || diskNumbers is null || diskNumbers.Count == 0) return false;
        return string.Equals(typed.Trim(), Expected(diskNumbers), StringComparison.Ordinal);
    }
}
