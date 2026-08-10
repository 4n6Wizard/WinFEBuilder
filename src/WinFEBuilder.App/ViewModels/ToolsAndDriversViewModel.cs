using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Services;

namespace WinFEBuilder.App.ViewModels;

public sealed class ToolsAndDriversViewModel
{
    private readonly IToolService _tools;
    private readonly IDriverService _drivers;
    private readonly ISettingsService _settings;
    private readonly IAdkDetectionService _adk;
    private readonly IImageContentService _content;

    public ToolsAndDriversViewModel(IToolService tools, IDriverService drivers, ISettingsService settings,
        IAdkDetectionService adk, IImageContentService content)
    {
        _tools = tools;
        _drivers = drivers;
        _settings = settings;
        _adk = adk;
        _content = content;
    }

    // Windows components are determined and installed automatically by the build (see
    // ToolComponentResolver + BuildService). There is intentionally no user-facing component API here.

    public string? FrameworkPath => _settings.Settings.LastFrameworkPath;

    // Tools → framework's USB\x86-x64\tools\<arch>
    public string? FrameworkToolsDir(string arch) =>
        string.IsNullOrWhiteSpace(FrameworkPath) ? null : _tools.ResolveFrameworkToolsDir(FrameworkPath!, arch);

    public Task<OperationResult> AddToolToFrameworkAsync(string toolSource, string arch, IProgress<string> p, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(FrameworkPath))
            return Task.FromResult(OperationResult.Fail("No framework selected.",
                recommendedAction: "Select and validate a framework on the Framework page first."));
        return _tools.AddToolToFrameworkAsync(toolSource, FrameworkPath!, arch, p, ct);
    }

    public IReadOnlyList<FrameworkTool> FrameworkTools() =>
        string.IsNullOrWhiteSpace(FrameworkPath) ? Array.Empty<FrameworkTool>() : _tools.ListFrameworkTools(FrameworkPath!);

    public void RemoveFrameworkTool(string path) => _tools.RemoveFrameworkTool(path);

    // Drivers
    public Task<List<DriverInfo>> EnumerateDriversAsync(string folder, string targetArch, CancellationToken ct) =>
        _drivers.EnumerateDriversAsync(folder, targetArch, WinFEBuilder.Core.Validation.InfOsApplicability.Adk1809Build, ct);
    public Task<DriverInjectionResult> InjectAsync(string bootWim, IEnumerable<DriverInfo> drivers, bool forceUnsigned, IProgress<string> p, CancellationToken ct) => _drivers.InjectAsync(bootWim, drivers, forceUnsigned, p, ct);
    public Task<string> MountedImagesAsync(CancellationToken ct) => _drivers.GetMountedImagesAsync(ct);
    public Task<bool> CleanupMountsAsync(CancellationToken ct) => _drivers.CleanupMountsAsync(ct);

    // ---------------------------------------------------------------- image content
    // Folders copied straight into boot.wim, for things WinPE has no package for. The driving case is
    // modern .NET: the components option installs .NET Framework 4.x, and nothing installs .NET
    // 5/6/8/9/10, so a tool built on it must have its runtime placed in the image.

    public ImageContentItem DescribeContent(string source, string destination, string? label = null) =>
        _content.Describe(source, destination, label);

    public Task<ImageContentResult> ApplyContentAsync(string bootWim, IEnumerable<ImageContentItem> items,
        bool compact, IProgress<string> p, CancellationToken ct) =>
        _content.ApplyAsync(bootWim, items, compact, p, ct);

    /// <summary>
    /// Builds the items for a .NET tool: the tool folder itself plus a matching runtime. Returns the
    /// reason instead when the pairing can't be satisfied, so the operator finds out here rather than
    /// from "You must install or update .NET" on a booted machine.
    /// </summary>
    public (List<ImageContentItem> Items, string? Problem, string? Summary) BuildDotNetToolPreset(
        string toolFolder, string toolDestination)
    {
        var items = new List<ImageContentItem>();

        if (!Directory.Exists(toolFolder))
            return (items, $"Folder not found: {toolFolder}", null);

        var required = DotNetRuntimeLocator.ReadRequiredFrameworkVersion(toolFolder);
        if (required is null)
        {
            // Not a modern .NET app — copy it as-is; it may be native or .NET Framework.
            items.Add(DescribeContent(toolFolder, toolDestination));
            return (items, null, "No runtimeconfig.json found — copying the folder only. " +
                                 "If this tool needs .NET Framework, use the Windows components option instead.");
        }

        var runtime = DotNetRuntimeLocator.SelectMatching(required);
        if (runtime is null)
        {
            var installed = DotNetRuntimeLocator.Enumerate()
                .Where(r => r.FrameworkName == DotNetRuntimeLocator.NetCoreApp)
                .Select(r => r.Version).OrderBy(v => v).ToList();
            return (items,
                $"This tool requires .NET {required}, and no matching runtime is installed on this machine. " +
                $"Installed: {(installed.Count > 0 ? string.Join(", ", installed) : "none")}. " +
                "Install that major version of the .NET runtime, then try again — the .NET host does not " +
                "roll forward across major versions.", null);
        }

        // Decide the Desktop Runtime from what the tool actually declares. Console tools like
        // aim_remote don't need it, and adding it costs ~75 MB of RAM at every WinPE boot — not a
        // judgement the operator should have to make.
        var needsDesktop = DotNetRuntimeLocator.RequiresDesktopRuntime(toolFolder);

        foreach (var (src, dest) in DotNetRuntimeLocator.BuildRuntimeLayout(
                     runtime, includeDesktopRuntime: needsDesktop))
        {
            var part = dest.Contains("host\\fxr", StringComparison.OrdinalIgnoreCase) ? "host"
                     : dest.Contains(DotNetRuntimeLocator.WindowsDesktopApp, StringComparison.OrdinalIgnoreCase) ? "desktop runtime"
                     : "runtime";
            items.Add(DescribeContent(src, dest, $".NET {runtime.Version} ({part})"));
        }

        var exe = DotNetRuntimeLocator.FindDotnetExe();
        if (exe is not null)
        {
            // dotnet.exe is a single file; stage its own folder so the tree copy has something to walk.
            var staging = Path.Combine(Path.GetTempPath(), $"winfe_dotnetexe_{Guid.NewGuid():N}");
            Directory.CreateDirectory(staging);
            File.Copy(exe, Path.Combine(staging, "dotnet.exe"), overwrite: true);
            items.Add(DescribeContent(staging, @"Program Files\dotnet", "dotnet.exe"));
        }

        items.Add(DescribeContent(toolFolder, toolDestination));

        var desktopNote = needsDesktop
            ? " Desktop Runtime included (the tool declares Microsoft.WindowsDesktop.App)."
            : " Desktop Runtime not needed — this is a console tool.";

        return (items, null,
            $"Tool requires .NET {required}; using installed runtime {runtime.Version}.{desktopNote}");
    }

    /// <summary>Locates an AIM Remote Agent folder: the framework's tools folder, or an Arsenal download.</summary>
    public string? FindAimRemoteFolder(string arch = "x64")
    {
        var candidates = new List<string>();

        var toolsDir = FrameworkToolsDir(arch);
        if (!string.IsNullOrWhiteSpace(toolsDir) && Directory.Exists(toolsDir))
            candidates.AddRange(Directory.EnumerateDirectories(toolsDir, $"AIM-Remote_{arch}", SearchOption.TopDirectoryOnly));

        try
        {
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloads))
            {
                foreach (var aim in Directory.EnumerateDirectories(downloads, "Arsenal-Image-Mounter*", SearchOption.TopDirectoryOnly))
                {
                    var remote = Path.Combine(aim, "remote", $"AIM-Remote_{arch}");
                    if (Directory.Exists(remote)) candidates.Add(remote);
                }
            }
        }
        catch { /* best effort */ }

        // Prefer the newest — a fresh framework ships an older agent than a current download, and the
        // agent version dictates which runtime is needed.
        return candidates.OrderByDescending(d =>
        {
            try { return Directory.GetLastWriteTimeUtc(d); } catch { return DateTime.MinValue; }
        }).FirstOrDefault();
    }

    public IReadOnlyList<string> WorkspaceBootWims()
    {
        try
        {
            var ws = _settings.Settings.WorkspaceRoot;
            if (!Directory.Exists(ws)) return Array.Empty<string>();
            return Directory.EnumerateFiles(ws, "boot.wim", SearchOption.AllDirectories)
                .Where(f => f.Replace('/', '\\').EndsWith(@"\sources\boot.wim", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }
}
