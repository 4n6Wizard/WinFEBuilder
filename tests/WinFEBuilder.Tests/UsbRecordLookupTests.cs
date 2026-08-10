using System.Text.Json;
using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Reports;
using WinFEBuilder.Core.Services;
using Xunit;

namespace WinFEBuilder.Tests;

/// <summary>
/// Covers locating and reading persisted usb-record files, which the Validation page uses to
/// pre-fill the USB serial instead of asking the examiner to re-type it.
/// </summary>
public class UsbRecordLookupTests
{
    private static ReportService Build(TempDir tmp, out AppPaths paths)
    {
        paths = new AppPaths(tmp.Path);
        var log = new LogService(tmp.Dir("logs"));
        var settings = new SettingsService(Path.Combine(tmp.Path, "settings.json"));
        settings.Settings.WorkspaceRoot = tmp.Dir("workspace");
        var adk = new AdkDetectionService(log);
        return new ReportService(paths, log, settings, adk);
    }

    private static string WriteUsbRecord(AppPaths paths, string fileName, string serial, DateTime lastWrite)
    {
        Directory.CreateDirectory(paths.ReportDir);
        var record = new UsbRecord
        {
            DiskNumber = 9,
            Model = "DataTraveler 3.0",
            SerialNumber = serial,
            BusType = "USB",
            AssignedDriveLetter = "I:",
            FinalStatus = "SUCCESS"
        };
        var path = Path.Combine(paths.ReportDir, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(record));
        File.SetLastWriteTimeUtc(path, lastWrite);
        return path;
    }

    [Fact]
    public void ListUsbRecords_NoReportsDirectory_ReturnsEmpty()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out _);
        Assert.Empty(svc.ListUsbRecords());
    }

    [Fact]
    public void ListUsbRecords_ReturnsNewestFirst()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out var paths);

        WriteUsbRecord(paths, "usb-record_2026-07-29_100000.json", "OLDSERIAL",
            new DateTime(2026, 7, 29, 10, 0, 0, DateTimeKind.Utc));
        WriteUsbRecord(paths, "usb-record_2026-07-29_160000.json", "NEWSERIAL",
            new DateTime(2026, 7, 29, 16, 0, 0, DateTimeKind.Utc));

        var records = svc.ListUsbRecords();

        Assert.Equal(2, records.Count);
        Assert.Equal("usb-record_2026-07-29_160000.json", Path.GetFileName(records[0]));
    }

    [Fact]
    public void ReadUsbRecord_RoundTripsSerialNumber()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out var paths);
        var path = WriteUsbRecord(paths, "usb-record_a.json", "6B00A2414BC9", DateTime.UtcNow);

        var record = svc.ReadUsbRecord(path);

        Assert.NotNull(record);
        Assert.Equal("6B00A2414BC9", record!.SerialNumber);
        Assert.Equal(9, record.DiskNumber);
    }

    [Fact]
    public void ReadUsbRecord_MissingFile_ReturnsNullRatherThanThrowing()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out var paths);
        Assert.Null(svc.ReadUsbRecord(Path.Combine(paths.ReportDir, "does-not-exist.json")));
    }

    [Fact]
    public void ReadUsbRecord_CorruptJson_ReturnsNullRatherThanThrowing()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out var paths);
        Directory.CreateDirectory(paths.ReportDir);
        var path = Path.Combine(paths.ReportDir, "usb-record_bad.json");
        File.WriteAllText(path, "{ this is not json");

        Assert.Null(svc.ReadUsbRecord(path));
    }

    [Fact]
    public void ListUsbRecords_IgnoresUnrelatedFiles()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out var paths);
        Directory.CreateDirectory(paths.ReportDir);
        File.WriteAllText(Path.Combine(paths.ReportDir, "report_2026-07-29.html"), "<html></html>");
        File.WriteAllText(Path.Combine(paths.ReportDir, "build-manifest.json"), "{}");
        WriteUsbRecord(paths, "usb-record_only.json", "S1", DateTime.UtcNow);

        var records = svc.ListUsbRecords();

        Assert.Single(records);
        Assert.Equal("usb-record_only.json", Path.GetFileName(records[0]));
    }
}
