using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Services;

/// <summary>
/// Copies folders into a boot.wim (mount → copy → commit), for content WinPE has no package for —
/// principally a modern .NET runtime and the tools that depend on it.
/// </summary>
public interface IImageContentService
{
    /// <summary>Counts files and bytes in a source folder so the UI can show the cost before applying.</summary>
    ImageContentItem Describe(string sourcePath, string destinationRelative, string? label = null);

    /// <summary>
    /// Mounts <paramref name="bootWimPath"/>, copies each selected item in, then commits. The mount
    /// is always released — commit on success, discard on any failure.
    /// </summary>
    /// <param name="compactAfterwards">
    /// Rebuild the image with /Export-Image after committing, dropping resources orphaned by
    /// servicing. Costs minutes; typically reclaims a large fraction of what servicing added.
    /// </param>
    Task<ImageContentResult> ApplyAsync(
        string bootWimPath,
        IEnumerable<ImageContentItem> items,
        bool compactAfterwards,
        IProgress<string>? progress,
        CancellationToken ct = default);
}
