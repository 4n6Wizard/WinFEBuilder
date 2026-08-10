namespace WinFEBuilder.Core.Models;

/// <summary>Whether a disk may be used as a USB target, with explicit reasons if not.</summary>
public sealed class DiskEligibility
{
    public int DiskNumber { get; init; }
    public bool CanTarget { get; init; }
    public IReadOnlyList<string> BlockReasons { get; init; } = Array.Empty<string>();

    public string BlockSummary => BlockReasons.Count == 0
        ? "Eligible as a target."
        : string.Join("; ", BlockReasons);
}

/// <summary>
/// The set of volumes/paths that must never be destroyed. Built from the running system and the
/// application's own settings (workspace, output, framework, ISO).
/// </summary>
public sealed class ProtectedContext
{
    /// <summary>Drive letters (e.g. "C:") that must be protected.</summary>
    public HashSet<string> ProtectedDriveLetters { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Human-readable reason per protected drive letter, for clear messaging.</summary>
    public Dictionary<string, string> ReasonByDriveLetter { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Protect(string? driveLetter, string reason)
    {
        if (string.IsNullOrWhiteSpace(driveLetter)) return;
        var key = Normalize(driveLetter);
        if (key is null) return;
        ProtectedDriveLetters.Add(key);
        if (!ReasonByDriveLetter.ContainsKey(key))
            ReasonByDriveLetter[key] = reason;
    }

    /// <summary>Normalize "C", "C:", "C:\" -> "C:".</summary>
    public static string? Normalize(string? letter)
    {
        if (string.IsNullOrWhiteSpace(letter)) return null;
        var c = letter.TrimStart().FirstOrDefault(char.IsLetter);
        return c == default ? null : $"{char.ToUpperInvariant(c)}:";
    }
}
