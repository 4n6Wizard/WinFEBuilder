namespace WinFEBuilder.Core.Validation;

/// <summary>
/// Pure (IO-free) framework validation helpers, kept separate from <c>FrameworkService</c>
/// so the classification/heuristic logic can be unit tested without touching the disk.
/// </summary>
public static class FrameworkValidator
{
    /// <summary>Batch build scripts commonly shipped with the WinFE framework.</summary>
    public static readonly string[] KnownBuildScripts =
    {
        "MakeWinFEx64-x86.bat",
        "Makex64-x86-CD.bat",
        "MakeWinFEx64.bat",
        "MakeWinFEx86.bat",
        "MakePE.bat"
    };

    /// <summary>Subdirectories expected inside a genuine framework folder.</summary>
    public static readonly string[] ExpectedSubdirectories =
    {
        "Drivers",
        "Programs",
        "Wallpaper"
    };

    public static bool IsBuildScript(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        var name = Path.GetFileName(fileName);

        if (KnownBuildScripts.Any(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Heuristic: a .bat whose name references WinFE / Make + PE / CD.
        if (!name.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)) return false;
        var lower = name.ToLowerInvariant();
        return lower.Contains("winfe")
            || (lower.StartsWith("make") && (lower.Contains("pe") || lower.Contains("cd") || lower.Contains("x64") || lower.Contains("x86")));
    }

    /// <summary>Recognizes WinFE component executables/config that indicate a genuine framework.</summary>
    public static bool IsFrameworkComponent(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        var lower = Path.GetFileName(fileName).ToLowerInvariant();
        return lower.EndsWith(".exe") || lower.EndsWith(".dll") || lower.EndsWith(".wim");
    }

    public static bool IsConfigFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        var lower = Path.GetFileName(fileName).ToLowerInvariant();
        return lower.EndsWith(".ini") || lower.EndsWith(".xml") || lower.EndsWith(".cfg")
            || lower.EndsWith(".txt") || lower.EndsWith(".json") || lower.EndsWith(".reg");
    }

    /// <summary>
    /// Detects likely double-nesting: no build scripts at the top level, but exactly one
    /// child directory that does contain build scripts (i.e. the user selected the parent).
    /// </summary>
    /// <param name="topLevelFileNames">File names directly inside the selected folder.</param>
    /// <param name="childDirectoriesWithScripts">
    /// Count of immediate child directories that themselves contain build scripts.
    /// </param>
    public static bool IsLikelyDoubleNested(
        IEnumerable<string> topLevelFileNames, int childDirectoriesWithScripts)
    {
        var hasTopLevelScripts = topLevelFileNames.Any(IsBuildScript);
        return !hasTopLevelScripts && childDirectoriesWithScripts >= 1;
    }

    /// <summary>Heuristic: does any discovered file name suggest x64 support?</summary>
    public static bool AppearsToSupportX64(IEnumerable<string> fileNames)
    {
        return fileNames.Any(f =>
        {
            var lower = Path.GetFileName(f).ToLowerInvariant();
            return lower.Contains("x64") || lower.Contains("amd64");
        });
    }
}
