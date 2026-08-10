using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Reports;

public interface IReportService
{
    /// <summary>List build-manifest.json files under the workspace (newest first).</summary>
    IReadOnlyList<string> ListBuildManifests();

    /// <summary>
    /// List persisted usb-record json files in the reports directory (newest first).
    /// </summary>
    IReadOnlyList<string> ListUsbRecords();

    /// <summary>
    /// Reads a persisted usb-record json file, or null if it is missing or unreadable.
    /// </summary>
    UsbRecord? ReadUsbRecord(string path);

    /// <summary>
    /// Generate an HTML report from a build manifest, optionally combined with an in-memory manual
    /// validation record and a USB record. Writes only the HTML file and returns its path.
    /// </summary>
    string Generate(
        string buildManifestPath,
        ValidationRecord? validation = null,
        string? usbRecordPath = null);

    /// <summary>Build the report model without writing files (used by tests/preview).</summary>
    ReportModel BuildModel(
        string? buildManifestPath,
        ValidationRecord? validation = null,
        string? usbRecordPath = null);
}
