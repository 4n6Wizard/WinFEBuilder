namespace WinFEBuilder.Core.Configuration;

/// <summary>
/// Resolves the application's on-disk folders. Prefers the folders that ship alongside the
/// executable (config/, logs/, etc.) so the tool is portable, falling back to the executable dir.
/// </summary>
public sealed class AppPaths
{
    public string BaseDir { get; }

    /// <summary>The folder the app treats as its root (the solution root in a dev checkout, otherwise
    /// the executable's folder). Portable data folders (workspace/output/reports/logs) live under it.</summary>
    public string RootDir { get; }

    public string ConfigDir { get; }

    /// <summary>Root of all logging — one subfolder per session lives under here.</summary>
    public string LogDir { get; }

    /// <summary>
    /// Timestamp identifying this run, e.g. <c>2026-08-07_132746</c>. Also the session folder's name.
    /// </summary>
    public string SessionStamp { get; }

    /// <summary>
    /// Everything logged during this run — the app log, the JSON log, and every DISM log — goes here.
    /// Grouping by session keeps one build's evidence together instead of scattering thirty files
    /// across one folder, and makes it obvious which logs belong to which build.
    /// </summary>
    public string SessionLogDir { get; }

    public string WorkspaceDir { get; }
    public string OutputDir { get; }
    public string ReportDir { get; }

    public string SettingsFile => Path.Combine(ConfigDir, "settings.json");
    public string BuildProfilesFile => Path.Combine(ConfigDir, "build-profiles.json");

    public AppPaths(string? baseDir = null, string? sessionStamp = null)
    {
        BaseDir = baseDir ?? AppContext.BaseDirectory;

        // If a repo-style layout is detected (…\src\WinFEBuilder.App\bin\…), walk up to the solution root.
        var solutionRoot = FindSolutionRoot(BaseDir);
        var root = solutionRoot ?? BaseDir;
        RootDir = root;

        ConfigDir = Path.Combine(root, "config");
        LogDir = Path.Combine(root, "logs");
        WorkspaceDir = Path.Combine(root, "workspace");
        OutputDir = Path.Combine(root, "output");
        ReportDir = Path.Combine(root, "reports");

        SessionStamp = sessionStamp ?? DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        SessionLogDir = Path.Combine(LogDir, SessionStamp);

        foreach (var d in new[] { ConfigDir, LogDir, SessionLogDir, WorkspaceDir, OutputDir, ReportDir })
            Directory.CreateDirectory(d);
    }

    private static string? FindSolutionRoot(string start)
    {
        try
        {
            var dir = new DirectoryInfo(start);
            for (int i = 0; i < 8 && dir is not null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "WinFEBuilder.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        catch { /* ignore */ }
        return null;
    }
}
