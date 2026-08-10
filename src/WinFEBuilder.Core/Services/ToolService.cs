using WinFEBuilder.Core.Hashing;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Validation;

namespace WinFEBuilder.Core.Services;

/// <summary>
/// Manages portable forensic tools by placing them into the framework's
/// <c>USB\x86-x64\tools\&lt;arch&gt;</c> folder, so the official build bakes them into the ISO and USB.
/// This is exactly the manual WinFE step (paste the tool folder into tools\x64), automated.
/// </summary>
public sealed class ToolService : IToolService
{
    private readonly ILogService _log;
    private readonly IHashService _hash;

    public ToolService(ILogService log, IHashService hash)
    {
        _log = log;
        _hash = hash;
    }

    public string? ResolveFrameworkToolsDir(string frameworkRoot, string arch)
    {
        if (!Directory.Exists(frameworkRoot)) return null;
        var a = NormalizeArch(arch);

        // Find the media root (folder with Boot/EFI/Sources), e.g. …\USB\x86-x64.
        string? mediaRoot;
        try
        {
            var dirs = Directory.EnumerateDirectories(frameworkRoot, "*", SearchOption.AllDirectories);
            mediaRoot = MediaLocator.FindMediaRoot(dirs);
        }
        catch { mediaRoot = null; }

        if (mediaRoot is null) return null;
        var toolsDir = Path.Combine(mediaRoot, "tools", a);
        Directory.CreateDirectory(toolsDir);
        return toolsDir;
    }

    public async Task<OperationResult> AddToolToFrameworkAsync(string toolSourceDir, string frameworkRoot, string arch, IProgress<string>? progress, CancellationToken ct = default)
    {
        var start = DateTimeOffset.Now;

        if (!Directory.Exists(toolSourceDir))
            return OperationResult.Fail("Tool source folder not found.", recommendedAction: "Pick the tool's folder.", start: start, finish: DateTimeOffset.Now);

        var toolsDir = ResolveFrameworkToolsDir(frameworkRoot, arch);
        if (toolsDir is null)
            return OperationResult.Fail(
                "Could not find the framework's media/tools folder (USB\\x86-x64\\tools).",
                recommendedAction: "Confirm you selected a valid, extracted WinFE framework on the Framework page.",
                start: start, finish: DateTimeOffset.Now);

        try
        {
            var name = SanitizeName(Path.GetFileName(toolSourceDir.TrimEnd('\\')));
            var dest = Path.Combine(toolsDir, name);
            Directory.CreateDirectory(dest);

            int count = 0; long bytes = 0;
            foreach (var file in Directory.EnumerateFiles(toolSourceDir, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var rel = PathValidator.GetRelativePath(toolSourceDir, file);
                var target = Path.Combine(dest, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
                count++;
                try { bytes += new FileInfo(target).Length; } catch { }
                if (count % 25 == 0)
                {
                    progress?.Report($"Copied {count} files…");
                    await Task.Yield(); // keep the UI responsive during large copies
                }
            }

            _log.Pass("Tools", $"Added tool '{name}' to framework: {dest} ({count} files, {bytes / 1024d / 1024d:F1} MB).");
            return OperationResult.Ok(
                $"Added '{name}' to the framework ({count} files, {bytes / 1024d / 1024d:F1} MB). It will be included on the next Build.",
                technical: dest, outputs: new[] { dest }, start: start, finish: DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            _log.Error("Tools", "Add tool to framework failed.", ex);
            return OperationResult.Fail("Failed to copy the tool into the framework.", technical: ex.Message,
                recommendedAction: "Check permissions and free space on the framework drive.", exception: ex.ToString(),
                start: start, finish: DateTimeOffset.Now);
        }
    }

    public IReadOnlyList<FrameworkTool> ListFrameworkTools(string frameworkRoot)
    {
        var list = new List<FrameworkTool>();
        foreach (var arch in new[] { "x64", "x86" })
        {
            var dir = ResolveFrameworkToolsDir(frameworkRoot, arch);
            if (dir is null || !Directory.Exists(dir)) continue;
            foreach (var toolDir in SafeGetDirectories(dir))
            {
                int count = 0; long bytes = 0;
                try
                {
                    foreach (var f in Directory.EnumerateFiles(toolDir, "*", SearchOption.AllDirectories))
                    {
                        count++;
                        try { bytes += new FileInfo(f).Length; } catch { }
                    }
                }
                catch { }
                list.Add(new FrameworkTool
                {
                    Name = Path.GetFileName(toolDir),
                    Architecture = arch,
                    Path = toolDir,
                    FileCount = count,
                    SizeBytes = bytes
                });
            }
        }
        return list;
    }

    public void RemoveFrameworkTool(string toolPath)
    {
        try
        {
            if (Directory.Exists(toolPath)) Directory.Delete(toolPath, recursive: true);
            _log.Info("Tools", $"Removed tool folder: {toolPath}");
        }
        catch (Exception ex)
        {
            _log.Error("Tools", $"Failed to remove tool: {toolPath}", ex);
            throw;
        }
    }

    private static string[] SafeGetDirectories(string dir)
    {
        try { return Directory.GetDirectories(dir); } catch { return Array.Empty<string>(); }
    }

    private static string NormalizeArch(string arch)
        => (arch ?? "").Contains("86") ? "x86" : "x64";

    private static string SanitizeName(string name)
    {
        var cleaned = new string(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "Tool" : cleaned;
    }
}
