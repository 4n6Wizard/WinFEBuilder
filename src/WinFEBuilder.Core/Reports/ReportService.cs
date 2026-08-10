using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Services;

namespace WinFEBuilder.Core.Reports;

/// <summary>
/// Generates the HTML build report. Operational/forensic statuses are taken ONLY from recorded
/// data — nothing is auto-marked as passed.
/// </summary>
public sealed class ReportService : IReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AppPaths _paths;
    private readonly ILogService _log;
    private readonly ISettingsService _settings;
    private readonly IAdkDetectionService _adk;

    public ReportService(AppPaths paths, ILogService log, ISettingsService settings, IAdkDetectionService adk)
    {
        _paths = paths;
        _log = log;
        _settings = settings;
        _adk = adk;
    }

    public IReadOnlyList<string> ListBuildManifests()
    {
        try
        {
            var ws = _settings.Settings.WorkspaceRoot;
            if (!Directory.Exists(ws)) return Array.Empty<string>();
            return Directory.GetFiles(ws, "build-manifest.json", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    public IReadOnlyList<string> ListUsbRecords()
    {
        try
        {
            if (!Directory.Exists(_paths.ReportDir)) return Array.Empty<string>();
            return Directory.GetFiles(_paths.ReportDir, "usb-record_*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    public UsbRecord? ReadUsbRecord(string path) => ReadJson<UsbRecord>(path);

    public ReportModel BuildModel(string? buildManifestPath, ValidationRecord? validation = null, string? usbRecordPath = null)
    {
        var model = new ReportModel
        {
            OperatorName = _settings.Settings.OperatorName,
            OrganizationName = _settings.Settings.OrganizationName,
            ComputerName = Environment.MachineName,
            WindowsVersion = RuntimeInformation.OSDescription,
            DotNetVersion = RuntimeInformation.FrameworkDescription,
            PowerShellVersion = DetectPowerShellVersion()
        };

        try
        {
            var adk = _adk.Detect();
            model.AdkVersion = adk.Version;
            model.WinPeArchitectures = adk.SupportedArchitectures.Count > 0 ? string.Join(", ", adk.SupportedArchitectures) : null;
            model.DismPath = adk.DismPath;
        }
        catch (Exception ex) { _log.Debug("Report", $"ADK detect failed: {ex.Message}"); }

        model.Build = ReadJson<BuildManifest>(buildManifestPath);
        model.Usb = ReadJson<UsbRecord>(usbRecordPath);
        model.Validation = validation;

        return model;
    }

    public string Generate(string buildManifestPath, ValidationRecord? validation = null, string? usbRecordPath = null)
    {
        var model = BuildModel(buildManifestPath, validation, usbRecordPath);

        Directory.CreateDirectory(_paths.ReportDir);
        var stamp = model.GeneratedLocal.ToString("yyyy-MM-dd_HHmmss");
        var htmlPath = Path.Combine(_paths.ReportDir, $"report_{stamp}.html");

        // HTML only: the report is the human-readable deliverable; no JSON side-files are written.
        File.WriteAllText(htmlPath, RenderHtml(model), Encoding.UTF8);

        _log.Pass("Report", $"Generated report: {htmlPath}");
        return htmlPath;
    }

    private static T? ReadJson<T>(string? path) where T : class
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions); }
        catch { return null; }
    }

    private static string? DetectPowerShellVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\PowerShell\3\PowerShellEngine");
            return key?.GetValue("PowerShellVersion") as string;
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------ HTML
    private static string RenderHtml(ReportModel m)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<title>WinFE Builder — Build Report</title><style>");
        sb.Append(@"
body{font-family:Segoe UI,Arial,sans-serif;color:#111827;margin:24px;background:#f9fafb}
h1{font-size:20px;margin-bottom:2px}h2{font-size:15px;margin-top:24px;border-bottom:1px solid #e5e7eb;padding-bottom:4px}
table{border-collapse:collapse;width:100%;margin-top:8px;background:#fff}
td,th{border:1px solid #e5e7eb;padding:6px 10px;text-align:left;vertical-align:top;font-size:13px}
th{background:#f3f4f6;width:260px}
.mono{font-family:Consolas,monospace;font-size:12px;word-break:break-all}
.pass{color:#166534;font-weight:600}.warn{color:#b45309;font-weight:600}.fail{color:#b91c1c;font-weight:600}.nt{color:#6b7280;font-weight:600}
.badge{display:inline-block;padding:2px 8px;border-radius:4px;font-size:12px}
small{color:#6b7280}
");
        sb.Append("</style></head><body>");
        sb.Append("<h1>WinFE Builder — Build Report</h1>");
        sb.Append($"<small>Generated {H(m.GeneratedLocal.ToString("yyyy-MM-dd HH:mm:ss"))} — App v{H(m.ApplicationVersion)}</small>");

        // Operational status (kept honest)
        sb.Append("<h2>Operational status (build vs. forensic)</h2><table>");
        Row(sb, "Build", Status(m.BuildStatus));
        Row(sb, "Boot Structure", Status(m.BootStructureStatus));
        Row(sb, "Boot Test", Status(m.BootTestStatus));
        Row(sb, "Write-Protection Test", Status(m.WriteProtectionTestStatus));
        sb.Append("</table>");
        sb.Append("<small>Boot / write-protection / approval are recorded only from manual validation — never auto-set.</small>");


        // Build
        if (m.Build is { } b)
        {
            sb.Append("<h2>Build</h2><table>");
            Row(sb, "WinFE Source", $"<span class='mono'>{H(b.FrameworkSource)}</span>");
            Row(sb, "Workspace", $"<span class='mono'>{H(b.WorkspacePath)}</span>");
            Row(sb, "Media script", H(b.MediaScript) + $" (exit {b.MediaBuildExitCode})");
            Row(sb, "ISO script", H(b.IsoScript) + $" (exit {b.IsoBuildExitCode})");
Row(sb, "ISO output", $"<span class='mono'>{H(b.IsoDestinationPath)}</span>");
            Row(sb, "ISO SHA-256", $"<span class='mono'>{H(b.IsoSha256)}</span>");
            Row(sb, "ISO size", $"{b.IsoSize / 1024d / 1024d:F0} MB");
            sb.Append("</table>");
            if (b.Warnings.Count > 0) sb.Append("<small><b>Warnings:</b> " + H(string.Join("; ", b.Warnings)) + "</small>");
        }

        // USB
        if (m.Usb is { } u)
        {
            sb.Append("<h2>USB</h2><table>");
            Row(sb, "Disk", $"#{u.DiskNumber} {H(u.Model)}");
            Row(sb, "Serial number", H(u.SerialNumber));
            Row(sb, "Unique ID", $"<span class='mono'>{H(u.UniqueId)}</span>");
            Row(sb, "Bus / capacity", $"{H(u.BusType)} / {u.CapacityBytes / 1024d / 1024d / 1024d:F1} GB");
            Row(sb, "Drive letter / label", $"{H(u.AssignedDriveLetter)} / {H(u.Label)}");
            Row(sb, "Files / bytes copied", $"{u.FilesCopied} / {u.BytesCopied:N0}");
            Row(sb, "USB Creation", Status(u.UsbCreationStatus));
            Row(sb, "Boot Structure", Status(u.BootStructureStatus));
            Row(sb, "Offline Structural Validation", Status(u.OfflineStructuralValidationStatus));
            sb.Append("</table>");
            if (u.CriticalHashes.Count > 0)
            {
                sb.Append("<table><tr><th>Critical file</th><th>SHA-256</th></tr>");
                foreach (var h in u.CriticalHashes)
                    sb.Append($"<tr><td class='mono'>{H(h.RelativePath)}</td><td class='mono'>{H(h.Sha256)}</td></tr>");
                sb.Append("</table>");
            }
        }

        // Validation
        if (m.Validation is { } v)
        {
            sb.Append("<h2>Manual validation record</h2><table>");
            Row(sb, "Booted UEFI", Check(v.BootedUefi));
            Row(sb, "Booted legacy BIOS", Check(v.BootedLegacyBios));
            Row(sb, "Internal source offline/read-only", Check(v.InternalSourceOfflineOrReadOnly));
Row(sb, "USB destination detected", Check(v.UsbDestinationDetected));
Row(sb, "Examiner", H(v.ExaminerName));
            Row(sb, "Test date", H(v.TestDate?.ToString("yyyy-MM-dd")));
sb.Append("</table>");
        }
        else
        {
            sb.Append("<h2>Manual validation record</h2><small>No validation record attached. Boot / write-protection tests remain NOT TESTED.</small>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void Row(StringBuilder sb, string k, string v) => sb.Append($"<tr><th>{H(k)}</th><td>{v}</td></tr>");

    private static string Status(string? s)
    {
        s ??= string.Empty; // guard against a null status in a partial/tampered record
        var cls = s.Contains("Pass", StringComparison.OrdinalIgnoreCase) || s.Contains("Successful", StringComparison.OrdinalIgnoreCase) || s.Contains("Validated", StringComparison.OrdinalIgnoreCase)
            ? "pass"
            : s.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ? "fail"
            : s.Contains("NOT", StringComparison.OrdinalIgnoreCase) ? "nt"
            : "warn";
        return $"<span class='badge {cls}'>{H(s)}</span>";
    }

    private static string Check(ManualCheck c) => c switch
    {
        ManualCheck.Pass => "<span class='badge pass'>PASS</span>",
        ManualCheck.Fail => "<span class='badge fail'>FAIL</span>",
        ManualCheck.NotApplicable => "<span class='badge nt'>N/A</span>",
        _ => "<span class='badge nt'>NOT TESTED</span>"
    };

    private static string H(string? s) => string.IsNullOrEmpty(s) ? "<small>—</small>" : WebUtility.HtmlEncode(s);
}
