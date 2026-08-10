using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Services;

namespace WinFEBuilder.App.ViewModels;

/// <summary>UI-agnostic wrapper over the environment audit. Keeps UI free of service logic.</summary>
public sealed class DashboardViewModel
{
    private readonly IEnvironmentService _environment;

    public DashboardViewModel(IEnvironmentService environment) => _environment = environment;

    public EnvironmentAuditResult? LastResult { get; private set; }

    public async Task<EnvironmentAuditResult> RunAuditAsync(CancellationToken ct)
    {
        LastResult = await _environment.RunAuditAsync(ct).ConfigureAwait(false);
        return LastResult;
    }
}
