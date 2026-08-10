using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Services;

namespace WinFEBuilder.App.ViewModels;

/// <summary>UI-agnostic wrapper over the build workflow and script discovery.</summary>
public sealed class BuildViewModel
{
    private readonly IBuildService _build;
    private readonly IFrameworkService _framework;
    private readonly ISettingsService _settings;

    public BuildViewModel(IBuildService build, IFrameworkService framework, ISettingsService settings)
    {
        _build = build;
        _framework = framework;
        _settings = settings;
    }

    public string? FrameworkPath => _settings.Settings.LastFrameworkPath;

    /// <summary>Validate the current framework and return its discovered build-script names.</summary>
    public async Task<(bool ok, string message, List<string> scripts)> DiscoverScriptsAsync(CancellationToken ct)
    {
        var path = FrameworkPath;
        if (string.IsNullOrWhiteSpace(path))
            return (false, "No framework selected. Set one on the Framework page.", new());

        var v = await _framework.ValidateAsync(path, ct).ConfigureAwait(false);
        if (!v.IsValid)
            return (false, $"Framework not valid: {v.Summary}", new());

        return (true, v.Summary, v.BuildScripts.Select(s => s.Name).Distinct().ToList());
    }

    public Task<BuildResult> RunBuildAsync(BuildRequest request, IProgress<string> progress, CancellationToken ct)
        => _build.RunBuildAsync(request, progress, ct);
}
