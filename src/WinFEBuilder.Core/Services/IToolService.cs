using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Services;

public interface IToolService
{
    /// <summary>
    /// Resolve the framework's tools folder for an architecture, i.e. the media root's
    /// <c>tools\&lt;arch&gt;</c> (e.g. <c>…\USB\x86-x64\tools\x64</c>). Creates it if missing.
    /// Returns null if a media root can't be located in the framework.
    /// </summary>
    string? ResolveFrameworkToolsDir(string frameworkRoot, string arch);

    /// <summary>
    /// Copy a portable tool folder into the framework's <c>tools\&lt;arch&gt;</c> so it becomes part
    /// of the build (baked into the ISO and copied to the USB). Hashes the copied files.
    /// </summary>
    Task<OperationResult> AddToolToFrameworkAsync(string toolSourceDir, string frameworkRoot, string arch, IProgress<string>? progress, CancellationToken ct = default);

    /// <summary>List tools currently present in the framework's tools\x64 and tools\x86 folders.</summary>
    IReadOnlyList<FrameworkTool> ListFrameworkTools(string frameworkRoot);

    /// <summary>Delete a tool folder from the framework's tools directory.</summary>
    void RemoveFrameworkTool(string toolPath);
}
