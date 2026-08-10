using WinFEBuilder.Core.Hashing;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Validation;

namespace WinFEBuilder.Core.Services;

/// <summary>
/// Read-only DISM operations. For Milestone 2 this inspects boot.wim without mounting it.
/// (Driver injection / mounting arrives in Milestone 4.)
/// </summary>
public sealed class DismService : IDismService
{
    private readonly ILogService _log;
    private readonly IProcessRunner _runner;
    private readonly IAdkDetectionService _adk;
    private readonly IHashService _hash;

    public DismService(ILogService log, IProcessRunner runner, IAdkDetectionService adk, IHashService hash)
    {
        _log = log;
        _runner = runner;
        _adk = adk;
        _hash = hash;
    }

    public string? ResolveDismPath()
    {
        var adk = _adk.Detect();
        if (adk.DismPath is not null && File.Exists(adk.DismPath)) return adk.DismPath;

        var inbox = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Dism.exe");
        return File.Exists(inbox) ? inbox : null;
    }

    public async Task<WimInfo> GetWimInfoAsync(string wimPath, CancellationToken ct = default)
    {
        var info = new WimInfo { WimPath = wimPath };

        if (!PathValidator.IsValidAbsolutePath(wimPath) || !File.Exists(wimPath))
        {
            info.Error = "boot.wim not found.";
            return info;
        }

        try { info.SizeBytes = new FileInfo(wimPath).Length; } catch { /* ignore */ }

        var dism = ResolveDismPath();
        if (dism is null)
        {
            info.Error = "DISM executable could not be located.";
            _log.Fail("DISM", info.Error);
            return info;
        }

        // Summary: list all images. /English forces parseable output.
        var listArgs = new[] { "/English", "/Get-WimInfo", $"/WimFile:{wimPath}" };
        var list = await _runner.RunAsync(dism, listArgs, timeoutMs: 120_000, ct: ct).ConfigureAwait(false);
        info.DismExitCode = list.ExitCode;
        info.DismRawOutput = list.StandardOutput;
        info.DismSucceeded = list.ExitCode == 0 && DismOutputParser.IndicatesSuccess(list.StandardOutput);

        if (!info.DismSucceeded)
        {
            info.Error = $"DISM /Get-WimInfo failed (exit {list.ExitCode}).";
            _log.Fail("DISM", info.Error);
            return info;
        }

        info.Images.AddRange(DismOutputParser.ParseImageList(list.StandardOutput));

        // Per-index detail for architecture (index 1).
        if (info.Images.Count > 0)
        {
            var idx = info.Images[0].Index;
            var detailArgs = new[] { "/English", "/Get-WimInfo", $"/WimFile:{wimPath}", $"/index:{idx}" };
            var detail = await _runner.RunAsync(dism, detailArgs, timeoutMs: 120_000, ct: ct).ConfigureAwait(false);
            info.Architecture = DismOutputParser.ParseArchitecture(detail.StandardOutput)
                                ?? info.Images[0].Architecture;
        }

        try
        {
            info.Sha256 = await _hash.ComputeSha256Async(wimPath, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Debug("DISM", $"boot.wim hash failed: {ex.Message}");
        }

        _log.Pass("DISM", $"boot.wim inspected: {info.ImageCount} image(s), arch {info.Architecture ?? "unknown"}.");
        return info;
    }
}
