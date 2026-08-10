namespace WinFEBuilder.Core.Validation;

/// <summary>
/// Validates the exact destructive-confirmation phrase the user must type before any
/// USB erase (Milestone 3). Included now so it is unit-tested from the start.
/// The required phrase is: ERASE DISK &lt;number&gt;
/// </summary>
public static class ConfirmationPhraseValidator
{
    /// <summary>Build the exact required phrase for a given disk number.</summary>
    public static string BuildExpectedPhrase(int diskNumber) => $"ERASE DISK {diskNumber}";

    /// <summary>
    /// Returns true only when <paramref name="typed"/> matches "ERASE DISK &lt;number&gt;" exactly,
    /// case-sensitive, allowing only surrounding whitespace. Any other disk number fails.
    /// </summary>
    public static bool IsValid(string? typed, int diskNumber)
    {
        if (typed is null) return false;
        var expected = BuildExpectedPhrase(diskNumber);
        return string.Equals(typed.Trim(), expected, StringComparison.Ordinal);
    }
}
