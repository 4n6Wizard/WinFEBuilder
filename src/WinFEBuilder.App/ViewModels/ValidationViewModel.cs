using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Reports;

namespace WinFEBuilder.App.ViewModels;

public sealed class ValidationViewModel
{
    private readonly IReportService _reports;

    public ValidationViewModel(IReportService reports) => _reports = reports;

    /// <summary>
    /// Generate the HTML report for the most recent build directly from the entered validation record.
    /// Nothing is persisted as a separate file. Throws if no completed build exists yet.
    /// </summary>
    /// <remarks>
    /// The most recent usb-record is passed through so the report includes the USB creation details
    /// (disk identity, stage results, boot-file hashes) rather than omitting that section.
    /// </remarks>
    public string GenerateReport(ValidationRecord record)
    {
        var manifest = _reports.ListBuildManifests().FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No completed build was found. Build a WinFE image first, then record its validation.");
        var usbRecordPath = _reports.ListUsbRecords().FirstOrDefault();
        return _reports.Generate(manifest, record, usbRecordPath);
    }

    /// <summary>
    /// Serial number of the most recently created USB, or null if none has been created.
    /// </summary>
    /// <remarks>
    /// Pre-filling this removes a hand-transcription step. The serial identifies the exact medium a
    /// validation report attests to, and re-typing a string like "6B00A2414BC9" into a case document
    /// is an easy place to transpose a character.
    /// </remarks>
    public string? LatestUsbSerial()
    {
        var path = _reports.ListUsbRecords().FirstOrDefault();
        if (string.IsNullOrEmpty(path)) return null;

        var record = _reports.ReadUsbRecord(path);
        return string.IsNullOrWhiteSpace(record?.SerialNumber) ? null : record!.SerialNumber!.Trim();
    }

    /// <summary>
    /// Id (folder name) of the most recent build, or null if none exists. Used to pre-fill the Build ID
    /// field so the report is tied to the build the operator just made.
    /// </summary>
    public string? LatestBuildId()
    {
        var newest = _reports.ListBuildManifests().FirstOrDefault();
        if (string.IsNullOrEmpty(newest)) return null;
        var dir = Path.GetDirectoryName(newest);
        return string.IsNullOrEmpty(dir) ? null : Path.GetFileName(dir);
    }
}
