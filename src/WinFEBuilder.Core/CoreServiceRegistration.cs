using Microsoft.Extensions.DependencyInjection;
using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Hashing;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Services;

namespace WinFEBuilder.Core;

/// <summary>Central DI wiring for the Core library.</summary>
public static class CoreServiceRegistration
{
    public static IServiceCollection AddWinFeBuilderCore(this IServiceCollection services, AppPaths paths)
    {
        services.AddSingleton(paths);

        // Log into this run's own folder, not the shared root — see AppPaths.SessionLogDir.
        services.AddSingleton<ILogService>(_ => new LogService(paths.SessionLogDir));
        services.AddSingleton<ISettingsService>(_ =>
        {
            var settings = new SettingsService(paths.SettingsFile);
            // Make the data folders portable: a relative/blank root resolves to a folder beside the
            // executable (via AppPaths.RootDir); an absolute path in settings.json is kept as an override.
            var s = settings.Settings;
            s.WorkspaceRoot = ResolveRoot(s.WorkspaceRoot, paths.RootDir, "workspace");
            s.OutputRoot = ResolveRoot(s.OutputRoot, paths.RootDir, "output");
            s.ReportRoot = ResolveRoot(s.ReportRoot, paths.RootDir, "reports");
            s.LogRoot = ResolveRoot(s.LogRoot, paths.RootDir, "logs");
            return settings;
        });

        services.AddSingleton<IHashService, HashService>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IAdkDetectionService, AdkDetectionService>();
        services.AddSingleton<IEnvironmentService, EnvironmentService>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<IFrameworkService, FrameworkService>();
        services.AddSingleton<IDismService, DismService>();
        services.AddSingleton<IBuildService, BuildService>();
        services.AddSingleton<IDiskService, DiskService>();
        services.AddSingleton<Reports.IReportService, Reports.ReportService>();
        services.AddSingleton<IProfileService>(_ => new ProfileService(paths.BuildProfilesFile));
        services.AddSingleton<IToolService, ToolService>();
        services.AddSingleton<IDriverService, DriverService>();
        services.AddSingleton<IImageContentService, ImageContentService>();
        services.AddSingleton<IWallpaperService, WallpaperService>();

        return services;
    }

    /// <summary>
    /// Resolve a data-folder root: a blank value falls back to <paramref name="fallbackFolder"/> beside
    /// the app; a relative value is combined with the app root; an absolute value is used unchanged.
    /// </summary>
    private static string ResolveRoot(string? value, string rootDir, string fallbackFolder)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Path.GetFullPath(Path.Combine(rootDir, fallbackFolder));
        return Path.IsPathFullyQualified(value)
            ? value
            : Path.GetFullPath(Path.Combine(rootDir, value));
    }
}
