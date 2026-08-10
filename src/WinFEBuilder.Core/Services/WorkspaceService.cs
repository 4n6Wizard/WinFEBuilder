using System.Text.Json;
using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Validation;

namespace WinFEBuilder.Core.Services;

public sealed class WorkspaceService : IWorkspaceService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ISettingsService _settings;
    private readonly ILogService _log;

    public WorkspaceService(ISettingsService settings, ILogService log)
    {
        _settings = settings;
        _log = log;
    }

    public string BuildWorkspaceName(DateTimeOffset nowLocal) =>
        $"Build_{nowLocal:yyyy-MM-dd_HHmmss}";

    public string CreateTimestampedWorkspace(DateTimeOffset? nowLocal = null)
    {
        var root = _settings.Settings.WorkspaceRoot;
        if (!PathValidator.IsValidAbsolutePath(root))
            throw new InvalidOperationException($"Workspace root is not a valid absolute path: '{root}'.");

        var name = BuildWorkspaceName(nowLocal ?? DateTimeOffset.Now);
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        _log.Info("Workspace", $"Created workspace: {dir}");
        return dir;
    }

    public string WriteManifest(string workspaceDir, WorkspaceManifest manifest)
    {
        PathValidator.EnsureExistingDirectory(workspaceDir);
        var path = Path.Combine(workspaceDir, "workspace-manifest.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions));
        _log.Info("Workspace", $"Wrote manifest: {path}");
        return path;
    }
}
