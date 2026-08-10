using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Services;

namespace WinFEBuilder.App.ViewModels;

public sealed class WallpaperViewModel
{
    private readonly IWallpaperService _wallpaper;
    private readonly ISettingsService _settings;

    public WallpaperViewModel(IWallpaperService wallpaper, ISettingsService settings)
    {
        _wallpaper = wallpaper;
        _settings = settings;
    }

    public string? FrameworkPath => _settings.Settings.LastFrameworkPath;

    public string? CurrentWallpaper() =>
        string.IsNullOrWhiteSpace(FrameworkPath) ? null : _wallpaper.CurrentWallpaper(FrameworkPath!);

    public IReadOnlyList<string> TargetPaths() =>
        string.IsNullOrWhiteSpace(FrameworkPath) ? Array.Empty<string>() : _wallpaper.TargetPaths(FrameworkPath!);

    public OperationResult SetWallpaper(string imageJpegPath)
    {
        if (string.IsNullOrWhiteSpace(FrameworkPath))
            return OperationResult.Fail("No framework selected.",
                recommendedAction: "Select and validate a framework on the Framework page first.");
        return _wallpaper.SetWallpaper(imageJpegPath, FrameworkPath!);
    }
}
