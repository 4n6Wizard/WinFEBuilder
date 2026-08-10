using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Services;

public interface IEnvironmentService
{
    /// <summary>Run the full environment audit using real detection code.</summary>
    Task<EnvironmentAuditResult> RunAuditAsync(CancellationToken ct = default);

    /// <summary>Whether the current process is elevated (Administrator).</summary>
    bool IsElevated();
}
