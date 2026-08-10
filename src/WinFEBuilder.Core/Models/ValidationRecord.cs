using System.Text.Json.Serialization;

namespace WinFEBuilder.Core.Models;

/// <summary>Tri-state result for a manual validation check. Defaults to NotTested.</summary>
public enum ManualCheck
{
    NotTested = 0,
    Pass = 1,
    Fail = 2,
    NotApplicable = 3
}

/// <summary>
/// A human-entered validation record for a WinFE build/USB. Every operational result here is
/// entered by a person — the application never sets these automatically.
/// </summary>
public sealed class ValidationRecord
{
    public string RecordVersion { get; init; } = "1.0";
    public DateTimeOffset CreatedLocal { get; init; } = DateTimeOffset.Now;

    /// <summary>Optional link to the build this validates (workspace folder or ISO/USB id).</summary>
    public string? BuildReference { get; set; }
    public string? UsbSerialNumber { get; set; }

    // Boot tests
    public ManualCheck BootedUefi { get; set; } = ManualCheck.NotTested;
    public ManualCheck BootedLegacyBios { get; set; } = ManualCheck.NotTested;

    // Write-protection / forensic soundness
    public ManualCheck InternalSourceOfflineOrReadOnly { get; set; } = ManualCheck.NotTested;
    public ManualCheck TestSourceHashMatchedBeforeAfter { get; set; } = ManualCheck.NotTested;

    // USB destination
    public ManualCheck UsbDestinationDetected { get; set; } = ManualCheck.NotTested;

    // People / dates
    public string? ExaminerName { get; set; }
    public DateTimeOffset? TestDate { get; set; }

    // Derived summaries used by the report. Not serialized - they are recomputed from the stored
    // checks above, so they never appear as separate fields in the saved validation record.

    /// <summary>True only if a person explicitly recorded a passing write-protection test.</summary>
    [JsonIgnore]
    public bool WriteProtectionVerified =>
        InternalSourceOfflineOrReadOnly == ManualCheck.Pass &&
        TestSourceHashMatchedBeforeAfter == ManualCheck.Pass;

    [JsonIgnore]
    public bool BootVerified =>
        BootedUefi == ManualCheck.Pass || BootedLegacyBios == ManualCheck.Pass;
}
