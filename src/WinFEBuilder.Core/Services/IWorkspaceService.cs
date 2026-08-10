using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Services;

public interface IWorkspaceService
{
    /// <summary>Create a new timestamped workspace directory under the workspace root.</summary>
    string CreateTimestampedWorkspace(DateTimeOffset? nowLocal = null);

    /// <summary>Build the timestamped folder name, e.g. Build_2026-07-20_143000.</summary>
    string BuildWorkspaceName(DateTimeOffset nowLocal);

    /// <summary>Write the workspace manifest JSON to the workspace directory; returns its path.</summary>
    string WriteManifest(string workspaceDir, WorkspaceManifest manifest);
}
