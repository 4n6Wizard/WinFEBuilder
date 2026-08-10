namespace WinFEBuilder.Core.Validation;

/// <summary>
/// Chooses which framework batch file is the media build vs. the ISO build. Pure and IO-free.
/// The user can always override the selection in the UI.
/// </summary>
public static class BuildScriptSelector
{
    /// <summary>
    /// Picks the ISO-building script from the candidate file names. Heuristic: a script whose
    /// name references CD/ISO/DVD. Returns null if none match.
    /// </summary>
    public static string? SelectIsoScript(IEnumerable<string> scriptNames)
    {
        var names = scriptNames?.ToList() ?? new List<string>();
        return names.FirstOrDefault(IsIsoScript);
    }

    /// <summary>
    /// Picks the media build script: prefer the canonical MakeWinFE* name; otherwise the first
    /// build script that is NOT an ISO script.
    /// </summary>
    public static string? SelectMediaScript(IEnumerable<string> scriptNames)
    {
        var names = scriptNames?.ToList() ?? new List<string>();

        var canonical = names.FirstOrDefault(n =>
            n.StartsWith("MakeWinFE", StringComparison.OrdinalIgnoreCase) && !IsIsoScript(n));
        if (canonical is not null) return canonical;

        return names.FirstOrDefault(n => !IsIsoScript(n)) ?? names.FirstOrDefault();
    }

    public static bool IsIsoScript(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var lower = System.IO.Path.GetFileName(name).ToLowerInvariant();
        return lower.Contains("cd") || lower.Contains("iso") || lower.Contains("dvd");
    }
}
