namespace WinFEBuilder.Core.Validation;

/// <summary>
/// One reusable normalization for drive-letter targets. Accepts <c>K</c>, <c>K:</c>, <c>K:\</c>
/// (any case, surrounding whitespace, or a rooted path such as <c>K:\some\dir</c>) and always
/// returns the canonical <c>K:</c> form. Use this everywhere an external command needs a
/// drive-letter target so no argument is ever built by blindly appending ':'.
/// </summary>
public static class DriveLetterNormalizer
{
    /// <summary>Return the canonical <c>X:</c> form (uppercase letter + single colon, no slash).</summary>
    public static string Normalize(string? input)
    {
        var s = (input ?? string.Empty).Trim();
        if (s.Length == 0)
            throw new ArgumentException("A drive letter is required.", nameof(input));

        var c = char.ToUpperInvariant(s[0]);
        if (!char.IsLetter(c))
            throw new ArgumentException($"'{input}' does not start with a drive letter.", nameof(input));

        // Reject values that look like a letter but are followed by something other than a
        // colon/backslash/end (e.g. "Kx") to avoid silently mis-parsing junk.
        if (s.Length > 1 && s[1] != ':' && s[1] != '\\' && s[1] != '/')
            throw new ArgumentException($"'{input}' is not a valid drive-letter target.", nameof(input));

        return c + ":";
    }

    /// <summary>Return the canonical volume root <c>X:\</c> form (for file-copy destinations).</summary>
    public static string Root(string? input) => Normalize(input) + "\\";

    /// <summary>Non-throwing variant; returns null when the value is not a drive letter.</summary>
    public static string? TryNormalize(string? input)
    {
        try { return Normalize(input); }
        catch (ArgumentException) { return null; }
    }
}
