using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Services;

public interface IWallpaperService
{
    /// <summary>The wallpaper.jpg target paths inside the framework's x64 and x86 staging folders.</summary>
    IReadOnlyList<string> TargetPaths(string frameworkRoot);

    /// <summary>Path to the current x64 wallpaper.jpg if it exists and is non-empty; otherwise null.</summary>
    string? CurrentWallpaper(string frameworkRoot);

    /// <summary>
    /// Copy an image into the framework as wallpaper.jpg in BOTH x64 and x86 staging folders, so the
    /// build bakes it in as the WinFE desktop wallpaper.
    /// </summary>
    OperationResult SetWallpaper(string imageJpegPath, string frameworkRoot);
}
