namespace WinFEBuilder.Core.Models;

/// <summary>
/// One folder to copy into a mounted boot.wim: a source on the host, and where it lands inside the
/// image.
/// <para>
/// This exists because WinPE has no package for modern .NET (5/6/8/9/10) — a tool built on it, such
/// as Arsenal Image Mounter's <c>aim_remote</c>, has to have its runtime placed in the image (or
/// beside the executable). Arsenal's documented procedure is exactly this: mount boot.wim, copy the
/// runtime to <c>Program Files\dotnet</c> and the tools to <c>Program Files\AIMTools</c>, commit.
/// </para>
/// </summary>
public sealed class ImageContentItem
{
    /// <summary>Folder on this machine to copy from.</summary>
    public string SourcePath { get; set; } = "";

    /// <summary>
    /// Destination relative to the image root, e.g. <c>Program Files\dotnet</c>. Always relative —
    /// an absolute or rooted path would escape the mount directory.
    /// </summary>
    public string DestinationRelative { get; set; } = "";

    /// <summary>Optional label shown in the UI, e.g. ".NET 9.0.18 runtime".</summary>
    public string? Label { get; set; }

    /// <summary>Whether this item is included in the next Apply.</summary>
    public bool Selected { get; set; } = true;

    public int FileCount { get; set; }
    public long Bytes { get; set; }

    public string SourceName => Path.GetFileName(SourcePath.TrimEnd(Path.DirectorySeparatorChar));

    public string Display =>
        $"{Label ?? SourceName} → \\{DestinationRelative.Trim('\\')}  ({FileCount} files, {Bytes / 1024d / 1024d:F1} MB)";

    /// <summary>
    /// Rejects destinations that would write outside the mounted image. Rooted paths, drive letters
    /// and <c>..</c> traversal are all refused — a mistake here would modify the host, not the image.
    /// </summary>
    public static bool IsSafeDestination(string? destination, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(destination)) { reason = "Destination is empty."; return false; }

        var d = destination.Replace('/', '\\').Trim();
        if (Path.IsPathRooted(d)) { reason = "Destination must be relative to the image root (no drive letter or leading \\)."; return false; }
        if (d.Split('\\').Any(p => p == "..")) { reason = "Destination must not contain '..'."; return false; }
        if (d.IndexOfAny(Path.GetInvalidPathChars()) >= 0) { reason = "Destination contains invalid characters."; return false; }

        return true;
    }
}
