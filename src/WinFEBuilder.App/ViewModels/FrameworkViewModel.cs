using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Services;

namespace WinFEBuilder.App.ViewModels;

/// <summary>UI-agnostic wrapper over framework validation and workspace copy.</summary>
public sealed class FrameworkViewModel
{
    private readonly IFrameworkService _framework;
    private readonly ISettingsService _settings;

    public FrameworkViewModel(IFrameworkService framework, ISettingsService settings)
    {
        _framework = framework;
        _settings = settings;
    }

    public FrameworkValidationResult? LastValidation { get; private set; }

    public string? LastFrameworkPath => _settings.Settings.LastFrameworkPath;

    public async Task<FrameworkValidationResult> ValidateAsync(string path, CancellationToken ct)
    {
        LastValidation = await _framework.ValidateAsync(path, ct).ConfigureAwait(false);
        if (LastValidation.IsValid)
        {
            _settings.Settings.LastFrameworkPath = LastValidation.SourcePath;
            _settings.Save();
        }
        return LastValidation;
    }

    public Task<OperationResult> CopyToWorkspaceAsync(IProgress<string>? progress, CancellationToken ct)
    {
        if (LastValidation is null || !LastValidation.IsValid)
            return Task.FromResult(OperationResult.Fail(
                "Validate a framework successfully before copying to the workspace.",
                recommendedAction: "Select and validate a framework first."));
        return _framework.CopyToWorkspaceAsync(LastValidation, progress, ct);
    }
}
