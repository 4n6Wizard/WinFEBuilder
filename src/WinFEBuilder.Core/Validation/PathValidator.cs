namespace WinFEBuilder.Core.Validation;

/// <summary>
/// Central path validation. All external file/path handling should pass through here so
/// paths are never blindly concatenated into commands and are always validated/normalized.
/// </summary>
public static class PathValidator
{
    /// <summary>True when the string is a syntactically valid, rooted, absolute path with no invalid chars.</summary>
    public static bool IsValidAbsolutePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        // Reject paths containing invalid characters.
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;

        try
        {
            // Must be absolute / rooted, not relative.
            if (!Path.IsPathRooted(path)) return false;
            var full = Path.GetFullPath(path);
            return !string.IsNullOrEmpty(full);
        }
        catch
        {
            return false;
        }
    }

    public static void EnsureExistingFile(string path)
    {
        if (!IsValidAbsolutePath(path))
            throw new ArgumentException($"Invalid file path: '{path}'.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: '{path}'.", path);
    }

    public static void EnsureExistingDirectory(string path)
    {
        if (!IsValidAbsolutePath(path))
            throw new ArgumentException($"Invalid directory path: '{path}'.", nameof(path));
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Directory not found: '{path}'.");
    }

    /// <summary>Directory-relative path using forward-agnostic normalization.</summary>
    public static string GetRelativePath(string baseDirectory, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory)) return Path.GetFileName(fullPath);
        try
        {
            return Path.GetRelativePath(baseDirectory, fullPath);
        }
        catch
        {
            return Path.GetFileName(fullPath);
        }
    }

    /// <summary>
    /// Quote a path for safe inclusion in a command line argument.
    /// Never used to build shell strings directly — arguments are passed via ArgumentList,
    /// but scripts occasionally require a quoted literal.
    /// </summary>
    public static string Quote(string path)
    {
        var p = path ?? string.Empty;
        // Escape embedded quotes and wrap.
        return "\"" + p.Replace("\"", "\\\"") + "\"";
    }

    /// <summary>True if <paramref name="child"/> is the same as or located under <paramref name="parent"/>.</summary>
    public static bool IsSameOrUnder(string parent, string child)
    {
        if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(child)) return false;
        try
        {
            var p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
            var c = Path.TrimEndingDirectorySeparator(Path.GetFullPath(child));
            if (string.Equals(p, c, StringComparison.OrdinalIgnoreCase)) return true;
            return c.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
