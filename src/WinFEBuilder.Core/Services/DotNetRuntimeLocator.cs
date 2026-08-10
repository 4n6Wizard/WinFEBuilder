using System.Text.Json;

namespace WinFEBuilder.Core.Services;

/// <summary>A shared framework installed on this machine, e.g. Microsoft.NETCore.App 9.0.18.</summary>
public sealed record InstalledRuntime(string FrameworkName, string Version, string Path)
{
    public int Major => System.Version.TryParse(Version, out var v) ? v.Major : 0;
    public bool IsDesktop => FrameworkName.Equals("Microsoft.WindowsDesktop.App", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Finds the .NET runtime a modern .NET tool needs, and matches it to what the tool actually asks
/// for.
/// <para>
/// WinPE ships no modern .NET, and <c>WinPE-NetFx</c> provides .NET <b>Framework</b> 4.x — a
/// different runtime entirely. So a tool like <c>aim_remote</c> needs its runtime supplied. The
/// version has to match the tool's <c>runtimeconfig.json</c> by <b>major</b> version: the host rolls
/// forward across patches but never across majors, which is why an AIM 3.12 agent (net10.0) fails
/// against a .NET 9 runtime with "You must install or update .NET to run this application".
/// </para>
/// </summary>
public static class DotNetRuntimeLocator
{
    public const string DefaultDotnetRoot = @"C:\Program Files\dotnet";
    public const string NetCoreApp = "Microsoft.NETCore.App";
    public const string WindowsDesktopApp = "Microsoft.WindowsDesktop.App";

    /// <summary>
    /// Every framework a published .NET app declares, across all runtimeconfig.json files in the
    /// folder. Reading the names (not just versions) is what lets the caller decide automatically
    /// whether the Desktop Runtime is needed, instead of asking the operator to know.
    /// </summary>
    public static List<(string Name, string Version)> ReadRequiredFrameworks(string appFolder)
    {
        var found = new List<(string, string)>();
        if (!Directory.Exists(appFolder)) return found;

        foreach (var cfg in Directory.EnumerateFiles(appFolder, "*.runtimeconfig.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(cfg));
                if (!doc.RootElement.TryGetProperty("runtimeOptions", out var opts)) continue;

                if (opts.TryGetProperty("framework", out var fx))
                    AddFramework(fx, found);

                if (opts.TryGetProperty("frameworks", out var many) && many.ValueKind == JsonValueKind.Array)
                    foreach (var f in many.EnumerateArray()) AddFramework(f, found);
            }
            catch { /* malformed config — keep looking */ }
        }

        return found;

        static void AddFramework(JsonElement element, List<(string, string)> into)
        {
            if (!element.TryGetProperty("name", out var n) || !element.TryGetProperty("version", out var v)) return;
            var name = n.GetString();
            var version = v.GetString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)) return;
            if (into.Any(x => x.Item1.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
            into.Add((name!, version!));
        }
    }

    /// <summary>
    /// True when the app needs the Desktop Runtime (WinForms/WPF). Console tools such as aim_remote
    /// and aim_cli do not, and including it anyway costs ~75 MB of RAM at every WinPE boot.
    /// </summary>
    public static bool RequiresDesktopRuntime(string appFolder) =>
        ReadRequiredFrameworks(appFolder)
            .Any(f => f.Name.Equals(WindowsDesktopApp, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads the framework version a published .NET app requires, or null if not a .NET app.</summary>
    public static string? ReadRequiredFrameworkVersion(string appFolder)
    {
        var frameworks = ReadRequiredFrameworks(appFolder);
        if (frameworks.Count == 0) return null;

        // The base runtime version governs which shared framework to install; the Desktop Runtime
        // ships in lockstep with it.
        var netCore = frameworks.FirstOrDefault(f => f.Name.Equals(NetCoreApp, StringComparison.OrdinalIgnoreCase));
        return netCore.Version ?? frameworks[0].Version;
    }

    /// <summary>Every shared framework version installed under <paramref name="dotnetRoot"/>.</summary>
    public static List<InstalledRuntime> Enumerate(string? dotnetRoot = null)
    {
        var root = string.IsNullOrWhiteSpace(dotnetRoot) ? DefaultDotnetRoot : dotnetRoot!;
        var list = new List<InstalledRuntime>();
        var sharedDir = Path.Combine(root, "shared");
        if (!Directory.Exists(sharedDir)) return list;

        foreach (var fx in Directory.EnumerateDirectories(sharedDir))
        {
            foreach (var ver in Directory.EnumerateDirectories(fx))
            {
                var name = Path.GetFileName(ver);
                if (Version.TryParse(name, out _))
                    list.Add(new InstalledRuntime(Path.GetFileName(fx), name, ver));
            }
        }

        return list;
    }

    /// <summary>
    /// Picks the newest installed runtime whose major version matches <paramref name="requiredVersion"/>.
    /// Returns null when nothing suitable is installed — the caller must not silently substitute a
    /// different major.
    /// </summary>
    public static InstalledRuntime? SelectMatching(
        string? requiredVersion,
        string frameworkName = NetCoreApp,
        string? dotnetRoot = null)
    {
        if (!Version.TryParse(requiredVersion, out var want)) return null;

        return Enumerate(dotnetRoot)
            .Where(r => r.FrameworkName.Equals(frameworkName, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.Major == want.Major)
            .OrderByDescending(r => Version.Parse(r.Version))
            .FirstOrDefault();
    }

    /// <summary>
    /// Builds the portable runtime layout a self-contained-style deployment needs:
    /// <c>dotnet.exe</c>, <c>host\fxr\&lt;ver&gt;</c> and <c>shared\&lt;framework&gt;\&lt;ver&gt;</c>.
    /// Copying the whole dotnet root instead would drag in every installed version plus the SDK.
    /// </summary>
    public static List<(string Source, string DestinationRelative)> BuildRuntimeLayout(
        InstalledRuntime runtime,
        string destinationRoot = @"Program Files\dotnet",
        bool includeDesktopRuntime = false,
        string? dotnetRoot = null)
    {
        var root = string.IsNullOrWhiteSpace(dotnetRoot) ? DefaultDotnetRoot : dotnetRoot!;
        var items = new List<(string, string)>();

        var fxr = Path.Combine(root, "host", "fxr", runtime.Version);
        if (Directory.Exists(fxr))
            items.Add((fxr, Path.Combine(destinationRoot, "host", "fxr", runtime.Version)));

        items.Add((runtime.Path, Path.Combine(destinationRoot, "shared", runtime.FrameworkName, runtime.Version)));

        if (includeDesktopRuntime)
        {
            var desktop = Path.Combine(root, "shared", WindowsDesktopApp, runtime.Version);
            if (Directory.Exists(desktop))
                items.Add((desktop, Path.Combine(destinationRoot, "shared", WindowsDesktopApp, runtime.Version)));
        }

        return items;
    }

    /// <summary>Path to dotnet.exe in an installation, or null if absent.</summary>
    public static string? FindDotnetExe(string? dotnetRoot = null)
    {
        var root = string.IsNullOrWhiteSpace(dotnetRoot) ? DefaultDotnetRoot : dotnetRoot!;
        var exe = Path.Combine(root, "dotnet.exe");
        return File.Exists(exe) ? exe : null;
    }
}
