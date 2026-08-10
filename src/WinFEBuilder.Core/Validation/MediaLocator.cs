namespace WinFEBuilder.Core.Validation;

/// <summary>
/// Pure helpers to locate build output (boot.wim media root, newest ISO) from a set of paths.
/// IO-free so the selection logic is unit tested; callers enumerate the disk and pass results in.
/// </summary>
public static class MediaLocator
{
    /// <summary>The expected boot components inside a WinFE media root.</summary>
    public static readonly string[] ExpectedBootComponents =
    {
        "Boot",
        "EFI",
        "Sources",
        @"Sources\boot.wim"
    };

    /// <summary>Find the first file whose path ends with <c>sources\boot.wim</c>.</summary>
    public static string? FindBootWim(IEnumerable<string> allFiles)
    {
        return allFiles?.FirstOrDefault(f =>
            f.Replace('/', '\\').EndsWith(@"\sources\boot.wim", StringComparison.OrdinalIgnoreCase)
            || f.Replace('/', '\\').Equals(@"sources\boot.wim", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Given a boot.wim path, return the media root (the folder that contains \sources).</summary>
    public static string? MediaRootFromBootWim(string? bootWimPath)
    {
        if (string.IsNullOrWhiteSpace(bootWimPath)) return null;
        // …\<root>\sources\boot.wim  ->  …\<root>
        var sourcesDir = Path.GetDirectoryName(bootWimPath);            // …\<root>\sources
        var root = sourcesDir is null ? null : Path.GetDirectoryName(sourcesDir);
        return root;
    }

    /// <summary>
    /// Find the deployable media root: the shallowest directory whose immediate children include
    /// Boot, EFI, and Sources. Works for both simple media and combined multi-arch media
    /// (e.g. IntelWinFE's USB\x86-x64, whose boot.wim lives in nested x64\sources and x86\sources).
    /// </summary>
    public static string? FindMediaRoot(IEnumerable<string> allDirectories)
    {
        var dirs = (allDirectories ?? Enumerable.Empty<string>())
            .Select(d => d.Replace('/', '\\').TrimEnd('\\'))
            .ToList();
        var set = new HashSet<string>(dirs, StringComparer.OrdinalIgnoreCase);

        return dirs
            .OrderBy(d => d.Length)
            .FirstOrDefault(d =>
                set.Contains(d + @"\Boot") &&
                set.Contains(d + @"\EFI") &&
                set.Contains(d + @"\Sources"));
    }

    /// <summary>True when a directory looks like a bootable media root (has Boot, EFI, Sources children).</summary>
    public static bool HasBootableSkeleton(IEnumerable<string> immediateChildDirNames)
    {
        var names = new HashSet<string>(
            (immediateChildDirNames ?? Enumerable.Empty<string>()).Select(n => n.Trim()),
            StringComparer.OrdinalIgnoreCase);
        return names.Contains("Boot") && names.Contains("EFI") && names.Contains("Sources");
    }

    /// <summary>Pick the newest, non-empty ISO from candidates.</summary>
    public static string? SelectNewestIso(IEnumerable<(string Path, long Size, DateTimeOffset LastWrite)> candidates)
    {
        return candidates?
            .Where(c => c.Size > 0)
            .OrderByDescending(c => c.LastWrite)
            .Select(c => c.Path)
            .FirstOrDefault();
    }
}
