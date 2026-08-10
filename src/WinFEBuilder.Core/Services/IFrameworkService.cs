using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Services;

public interface IFrameworkService
{
    /// <summary>Validate a selected framework directory (existence, scripts, structure, x64, hashes).</summary>
    Task<FrameworkValidationResult> ValidateAsync(string frameworkPath, CancellationToken ct = default);

    /// <summary>
    /// Copy the validated framework into a new timestamped workspace WITHOUT modifying the
    /// original, computing hashes and writing a manifest. Returns the operation result and
    /// (on success) the created workspace path via <see cref="OperationResult.OutputPaths"/>.
    /// </summary>
    Task<OperationResult> CopyToWorkspaceAsync(
        FrameworkValidationResult validation,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}
