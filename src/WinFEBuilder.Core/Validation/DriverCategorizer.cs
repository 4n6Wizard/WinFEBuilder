using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Validation;

/// <summary>
/// Groups discovered .inf drivers into user-friendly hardware categories for display. This is a
/// presentation-only classification — it does not change driver selection or injection behavior.
/// </summary>
public static class DriverCategorizer
{
    public const string Storage = "Storage Controllers";
    public const string Nvme = "NVMe";
    public const string Usb = "USB Controllers";
    public const string Network = "Network Adapters";
    public const string Display = "Display Adapters";
    public const string Other = "Other";

    /// <summary>Preferred display order.</summary>
    public static readonly string[] Order = { Storage, Nvme, Usb, Network, Display, Other };

    public static string Categorize(DriverInfo d)
    {
        if (d is null) return Other;
        var cls = (d.DriverClass ?? "").ToLowerInvariant();
        var hay = $"{d.InfName} {d.Provider} {d.DriverClass}".ToLowerInvariant();

        if (hay.Contains("nvme") || hay.Contains("nvm express")) return Nvme;
        if (cls.Contains("net")) return Network;
        if (cls.Contains("display") || cls.Contains("video")) return Display;
        if (cls.Contains("usb")) return Usb;
        if (cls is "hdc" or "scsiadapter" or "diskdrive"
            || cls.Contains("storage")
            || hay.Contains("raid") || hay.Contains("sata") || hay.Contains("ahci") || hay.Contains("scsi")
            || hay.Contains("storahci") || hay.Contains("storport"))
            return Storage;
        return Other;
    }
}
