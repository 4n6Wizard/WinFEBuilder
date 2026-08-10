using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Validation;

namespace WinFEBuilder.Core.Services;

/// <summary>
/// Sets the WinFE desktop wallpaper by copying an image as <c>wallpaper.jpg</c> into the framework's
/// <c>x64</c> and <c>x86</c> staging folders. The build batch copies wallpaper.jpg into the image and
/// points the registry at it, so it becomes the WinFE desktop background.
/// </summary>
public sealed class WallpaperService : IWallpaperService
{
    private const string WallpaperFileName = "wallpaper.jpg";
    private readonly ILogService _log;

    public WallpaperService(ILogService log) => _log = log;

    public IReadOnlyList<string> TargetPaths(string frameworkRoot) => new[]
    {
        Path.Combine(frameworkRoot, "x64", WallpaperFileName),
        Path.Combine(frameworkRoot, "x86", WallpaperFileName),
    };

    public string? CurrentWallpaper(string frameworkRoot)
    {
        var x64 = Path.Combine(frameworkRoot, "x64", WallpaperFileName);
        try { return File.Exists(x64) && new FileInfo(x64).Length > 0 ? x64 : null; }
        catch { return null; }
    }

    public OperationResult SetWallpaper(string imageJpegPath, string frameworkRoot)
    {
        var start = DateTimeOffset.Now;

        if (!PathValidator.IsValidAbsolutePath(imageJpegPath) || !File.Exists(imageJpegPath))
            return OperationResult.Fail("Image file not found.", recommendedAction: "Pick a valid image file.",
                start: start, finish: DateTimeOffset.Now);

        if (!Directory.Exists(frameworkRoot))
            return OperationResult.Fail("Framework folder not found.",
                recommendedAction: "Select and validate a framework on the Framework page first.",
                start: start, finish: DateTimeOffset.Now);

        var warnings = new List<string>();
        var outputs = new List<string>();
        try
        {
            foreach (var target in TargetPaths(frameworkRoot))
            {
                var dir = Path.GetDirectoryName(target)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    warnings.Add($"Created missing staging folder: {dir}");
                }
                File.Copy(imageJpegPath, target, overwrite: true);
                outputs.Add(target);
                _log.Info("Wallpaper", $"Wrote wallpaper: {target}");
            }

            _log.Pass("Wallpaper", "Wallpaper set for x64 and x86. It will apply on the next Build.");
            return OperationResult.Ok(
                "Wallpaper set (x64 + x86). It will be applied to the WinFE desktop on the next Build.",
                technical: string.Join(Environment.NewLine, outputs),
                outputs: outputs, warnings: warnings.Count > 0 ? warnings : null,
                start: start, finish: DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            _log.Error("Wallpaper", "Failed to set wallpaper.", ex);
            return OperationResult.Fail("Failed to write wallpaper into the framework.", technical: ex.Message,
                recommendedAction: "Check permissions on the framework folder.", exception: ex.ToString(),
                start: start, finish: DateTimeOffset.Now);
        }
    }
}
