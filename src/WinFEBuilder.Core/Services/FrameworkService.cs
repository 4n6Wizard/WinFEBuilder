using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Hashing;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Validation;

namespace WinFEBuilder.Core.Services;

/// <summary>
/// Validates a WinFE framework folder and copies it into a controlled workspace.
/// The original framework is treated as read-only and is never modified.
/// </summary>
public sealed class FrameworkService : IFrameworkService
{
    private readonly ILogService _log;
    private readonly IHashService _hash;
    private readonly IWorkspaceService _workspace;
    private readonly ISettingsService _settings;

    public FrameworkService(ILogService log, IHashService hash, IWorkspaceService workspace, ISettingsService settings)
    {
        _log = log;
        _hash = hash;
        _workspace = workspace;
        _settings = settings;
    }

    public async Task<FrameworkValidationResult> ValidateAsync(string frameworkPath, CancellationToken ct = default)
    {
        _log.Info("WinFE Source", $"Validating framework: {frameworkPath}");

        var result = new FrameworkValidationResult { SourcePath = frameworkPath ?? string.Empty };

        if (!PathValidator.IsValidAbsolutePath(frameworkPath))
        {
            result.Status = CheckStatus.Fail;
            result.Summary = "The path is not a valid absolute path.";
            result.RecommendedAction = "Select a valid folder using the Browse button.";
            _log.Fail("WinFE Source", result.Summary);
            return result;
        }

        var full = Path.GetFullPath(frameworkPath!);
        result.DirectoryExists = Directory.Exists(full);
        if (!result.DirectoryExists)
        {
            result.Status = CheckStatus.Fail;
            result.Summary = "The selected directory does not exist.";
            result.RecommendedAction = "Select the extracted WinFE framework folder.";
            _log.Fail("WinFE Source", result.Summary);
            return result;
        }

        // Readability
        List<string> topLevelFiles;
        List<string> topLevelDirs;
        try
        {
            topLevelFiles = Directory.EnumerateFiles(full, "*", SearchOption.TopDirectoryOnly).ToList();
            topLevelDirs = Directory.EnumerateDirectories(full).ToList();
            result.DirectoryReadable = true;
        }
        catch (Exception ex)
        {
            result.Status = CheckStatus.Fail;
            result.Summary = "The directory could not be read.";
            result.RecommendedAction = "Check folder permissions or run as Administrator.";
            result.Warnings.Add(ex.Message);
            _log.Error("WinFE Source", "Framework directory not readable.", ex);
            return result;
        }

        ct.ThrowIfCancellationRequested();

        // Detect double nesting: no top-level scripts, but a child dir that contains scripts.
        int childDirsWithScripts = 0;
        foreach (var d in topLevelDirs)
        {
            try
            {
                if (Directory.EnumerateFiles(d, "*.bat", SearchOption.TopDirectoryOnly)
                             .Any(f => FrameworkValidator.IsBuildScript(f)))
                    childDirsWithScripts++;
            }
            catch { /* ignore unreadable child */ }
        }
        result.PossibleDoubleNesting = FrameworkValidator.IsLikelyDoubleNested(
            topLevelFiles.Select(Path.GetFileName)!, childDirsWithScripts);

        // Gather all files (bounded recursion) for scripts/components/config.
        List<string> allFiles;
        try
        {
            allFiles = SafeEnumerateFiles(full).ToList();
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Partial enumeration: {ex.Message}");
            allFiles = topLevelFiles;
        }

        foreach (var f in allFiles)
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(f);
            var rel = PathValidator.GetRelativePath(full, f);
            long size;
            try { size = new FileInfo(f).Length; } catch { size = -1; }

            if (FrameworkValidator.IsBuildScript(name))
            {
                result.BuildScripts.Add(new DiscoveredFile
                {
                    Name = name, FullPath = f, RelativePath = rel, SizeBytes = size, Category = "BuildScript"
                });
            }
            else if (FrameworkValidator.IsFrameworkComponent(name))
            {
                result.Components.Add(new DiscoveredFile
                {
                    Name = name, FullPath = f, RelativePath = rel, SizeBytes = size, Category = "Component"
                });
            }
            else if (FrameworkValidator.IsConfigFile(name) && IsTopLevelOrShallow(full, f))
            {
                result.ConfigFiles.Add(new DiscoveredFile
                {
                    Name = name, FullPath = f, RelativePath = rel, SizeBytes = size, Category = "Config"
                });
            }
        }

        // Expected items
        foreach (var known in FrameworkValidator.KnownBuildScripts)
        {
            if (result.BuildScripts.Any(s => string.Equals(s.Name, known, StringComparison.OrdinalIgnoreCase)))
                result.ExpectedItemsFound.Add(known);
        }
        foreach (var sub in FrameworkValidator.ExpectedSubdirectories)
        {
            var present = topLevelDirs.Any(d => string.Equals(Path.GetFileName(d), sub, StringComparison.OrdinalIgnoreCase));
            if (present) result.ExpectedItemsFound.Add(sub + "\\");
            else result.ExpectedItemsMissing.Add(sub + "\\");
        }

        // Zero-byte build scripts are a hard failure.
        var zeroByteScripts = result.BuildScripts.Where(s => s.IsZeroBytes).ToList();
        foreach (var z in zeroByteScripts)
            result.Warnings.Add($"Build script is zero bytes: {z.RelativePath}");

        // x64 support heuristic
        result.SupportsX64 = FrameworkValidator.AppearsToSupportX64(
            result.BuildScripts.Select(s => s.Name)
                .Concat(result.Components.Select(c => c.Name)));

        // Compute hashes for scripts, components, config files.
        await ComputeHashesAsync(full, result.BuildScripts, ct).ConfigureAwait(false);
        await ComputeHashesAsync(full, result.Components, ct).ConfigureAwait(false);
        await ComputeHashesAsync(full, result.ConfigFiles, ct).ConfigureAwait(false);

        // Decide overall status.
        EvaluateStatus(result, zeroByteScripts.Count);

        _log.Log(new LogEntry
        {
            Severity = result.Status == CheckStatus.Pass ? LogSeverity.Pass
                     : result.Status == CheckStatus.Warning ? LogSeverity.Warning : LogSeverity.Fail,
            Operation = "WinFE Source",
            Message = $"Validation: {result.Status}. {result.Summary}",
            RelatedPath = full
        });

        return result;
    }

    private static void EvaluateStatus(FrameworkValidationResult r, int zeroByteScriptCount)
    {
        if (r.BuildScripts.Count == 0)
        {
            r.IsValid = false;
            r.Status = CheckStatus.Fail;
            r.Summary = r.PossibleDoubleNesting
                ? "No build scripts found here, but a nested subfolder appears to contain them."
                : "No WinFE build scripts (.bat) were found in this folder.";
            r.RecommendedAction = r.PossibleDoubleNesting
                ? "Select the inner extracted framework folder rather than its parent."
                : "Select the extracted WinFE framework root that contains the MakeWinFE*.bat files.";
            return;
        }

        if (zeroByteScriptCount > 0)
        {
            r.IsValid = false;
            r.Status = CheckStatus.Fail;
            r.Summary = $"{zeroByteScriptCount} build script(s) are zero bytes - the framework may be corrupt.";
            r.RecommendedAction = "Re-extract the WinFE framework and select the folder again.";
            return;
        }

        if (!r.SupportsX64)
        {
            r.IsValid = true;
            r.Status = CheckStatus.Warning;
            r.Summary = $"Found {r.BuildScripts.Count} build script(s), but x64 support could not be confirmed.";
            r.RecommendedAction = "Confirm the framework includes x64/amd64 build scripts before building x64 media.";
            return;
        }

        // Optional convenience folders (Drivers\ Programs\ Wallpaper\) vary by framework variant and
        // are NOT required to build. Their absence is informational only - it does not lower status.
        r.IsValid = true;
        r.Status = CheckStatus.Pass;
        r.Summary = $"Framework validated: {r.BuildScripts.Count} build script(s), "
                    + $"{r.Components.Count} component(s), x64 supported.";
        if (r.ExpectedItemsMissing.Count > 0)
            r.Summary += $"  (Optional folders not present: {string.Join(", ", r.ExpectedItemsMissing)} - normal for some variants.)";
    }

    public async Task<OperationResult> CopyToWorkspaceAsync(
        FrameworkValidationResult validation,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var start = DateTimeOffset.Now;

        if (validation is null || !validation.IsValid)
        {
            return OperationResult.Fail(
                "Framework is not valid; cannot copy to workspace.",
                recommendedAction: "Validate the framework successfully first.",
                start: start, finish: DateTimeOffset.Now);
        }

        var source = validation.SourcePath;
        if (!Directory.Exists(source))
        {
            return OperationResult.Fail(
                "Source framework directory no longer exists.",
                recommendedAction: "Re-select the framework folder.",
                start: start, finish: DateTimeOffset.Now);
        }

        try
        {
            var workspace = _workspace.CreateTimestampedWorkspace();
            var destRoot = Path.Combine(workspace, "framework");
            Directory.CreateDirectory(destRoot);
            progress?.Report($"Created workspace: {workspace}");

            // Replicate the FULL directory structure first, including EMPTY directories.
            // WinFE frameworks ship empty placeholder folders (e.g. USB\...\sources) that the
            // build batch copies boot.wim into; if we skipped them the build would fail with
            // "The system cannot find the path specified."
            int emptyDirs = 0;
            foreach (var dir in SafeEnumerateDirectories(source))
            {
                ct.ThrowIfCancellationRequested();
                var relDir = PathValidator.GetRelativePath(source, dir);
                Directory.CreateDirectory(Path.Combine(destRoot, relDir));
                emptyDirs++;
            }
            progress?.Report($"Replicated {emptyDirs} directories (including empty placeholders).");

            // Copy tree (original untouched).
            var files = SafeEnumerateFiles(source).ToList();
            long totalBytes = 0;
            int count = 0;
            var hashes = new List<FileHashEntry>();

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var rel = PathValidator.GetRelativePath(source, file);
                var dest = Path.Combine(destRoot, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: true);

                var entry = await _hash.ComputeEntryAsync(dest, destRoot, ct).ConfigureAwait(false);
                hashes.Add(entry);
                totalBytes += entry.SizeBytes;
                count++;

                if (count % 25 == 0)
                    progress?.Report($"Copied {count} files...");
            }

            progress?.Report($"Copied {count} files ({totalBytes / 1024d / 1024d:F1} MB). Writing manifest...");

            var manifest = new WorkspaceManifest
            {
                OriginalSourcePath = Path.GetFullPath(source),
                WorkspacePath = workspace,
                FileCount = count,
                TotalBytes = totalBytes,
                ComputerName = Environment.MachineName,
                Operator = _settings.Settings.OperatorName,
                Framework = new FrameworkMetadata
                {
                    SupportsX64 = validation.SupportsX64,
                    BuildScripts = validation.BuildScripts.Select(s => s.RelativePath).ToList(),
                    Components = validation.Components.Select(c => c.RelativePath).ToList(),
                    Warnings = validation.Warnings.ToList()
                }
            };
            manifest.Hashes.AddRange(hashes);

            var manifestPath = _workspace.WriteManifest(workspace, manifest);
            var finish = DateTimeOffset.Now;

            _log.Pass("WinFE Source", $"Copied framework to workspace: {workspace} ({count} files).");

            return OperationResult.Ok(
                $"Copied {count} files ({totalBytes / 1024d / 1024d:F1} MB) to workspace.",
                technical: $"Workspace: {workspace}\nManifest: {manifestPath}",
                outputs: new[] { workspace, destRoot, manifestPath },
                warnings: validation.Warnings.Count > 0 ? validation.Warnings.ToList() : null,
                start: start, finish: finish);
        }
        catch (OperationCanceledException)
        {
            _log.Warning("WinFE Source", "Copy to workspace was canceled.");
            return OperationResult.Fail("Copy canceled by user.", start: start, finish: DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            _log.Error("WinFE Source", "Copy to workspace failed.", ex);
            return OperationResult.Fail(
                "Failed to copy framework to workspace.",
                technical: ex.Message,
                recommendedAction: "Check available disk space and permissions on the workspace root.",
                exception: ex.ToString(),
                start: start, finish: DateTimeOffset.Now);
        }
    }

    private async Task ComputeHashesAsync(string baseDir, List<DiscoveredFile> files, CancellationToken ct)
    {
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            if (f.IsZeroBytes || f.SizeBytes < 0) continue;
            try
            {
                f.Sha256 = await _hash.ComputeSha256Async(f.FullPath, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Debug("WinFE Source", $"Hash failed for {f.RelativePath}: {ex.Message}");
            }
        }
    }

    private static bool IsTopLevelOrShallow(string root, string file)
    {
        var rel = Path.GetRelativePath(root, file);
        // Depth 0 (top level) or 1 (one subfolder deep).
        return rel.Count(c => c == Path.DirectorySeparatorChar) <= 1;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            string[] subDirs = Array.Empty<string>();
            try { subDirs = Directory.GetDirectories(dir); } catch { /* skip */ }
            foreach (var s in subDirs) pending.Push(s);

            string[] files = Array.Empty<string>();
            try { files = Directory.GetFiles(dir); } catch { /* skip */ }
            foreach (var f in files) yield return f;
        }
    }

    /// <summary>Enumerate every directory under <paramref name="root"/> (including empty ones).</summary>
    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            string[] subDirs = Array.Empty<string>();
            try { subDirs = Directory.GetDirectories(dir); } catch { /* skip */ }
            foreach (var s in subDirs)
            {
                yield return s;
                pending.Push(s);
            }
        }
    }
}
