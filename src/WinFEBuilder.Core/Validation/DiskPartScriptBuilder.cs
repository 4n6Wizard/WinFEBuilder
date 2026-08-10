namespace WinFEBuilder.Core.Validation;

/// <summary>Builds the DiskPart script for the standard WinFE USB layout. Pure and testable.</summary>
public static class DiskPartScriptBuilder
{
    /// <summary>Sanitize a FAT32 volume label (max 11 chars, no invalid characters).</summary>
    public static string SanitizeLabel(string? label)
    {
        var l = (label ?? "WINFE").Trim().ToUpperInvariant();
        var cleaned = new string(l.Where(c => char.IsLetterOrDigit(c)).ToArray());
        if (cleaned.Length == 0) cleaned = "WINFE";
        return cleaned.Length > 11 ? cleaned[..11] : cleaned;
    }

    /// <summary>
    /// Build the standard MBR/FAT32 bootable layout script for the verified disk number.
    /// The disk number MUST already have been verified by the caller.
    /// </summary>
    public static string Build(int diskNumber, string? label = "WINFE")
    {
        if (diskNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(diskNumber), "Disk number must be non-negative.");

        var lbl = SanitizeLabel(label);
        // Deterministic, explicit selection sequence. Do NOT rely on implicit partition/volume focus
        // after 'create partition' — on some devices focus is lost (observed: Disk 11 "There is no
        // volume selected"), so 'select partition 1' is issued before any command that needs a target.
        return string.Join(Environment.NewLine, new[]
        {
            "rescan",
            $"select disk {diskNumber}",
            "clean",
            "convert mbr",
            "create partition primary",
            "select partition 1",
            $"format fs=fat32 quick label={lbl}",
            "active",
            "assign",
            "exit"
        }) + Environment.NewLine;
    }
}
