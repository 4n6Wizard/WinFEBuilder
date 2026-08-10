using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Validation;

/// <summary>Compares disk identities to detect disk-number reassignment or device swaps.</summary>
public static class DiskIdentity
{
    /// <summary>
    /// True only when the two disks share the same stable identity (number, model, serial, unique
    /// id, size, bus type). Used to abort a destructive action if anything changed since selection.
    /// </summary>
    public static bool Matches(DiskInfo a, DiskInfo b)
    {
        if (a is null || b is null) return false;
        return string.Equals(a.IdentitySignature, b.IdentitySignature, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Return the specific fields that differ, for clear messaging.</summary>
    public static List<string> Differences(DiskInfo selected, DiskInfo current)
    {
        var diffs = new List<string>();
        if (selected is null || current is null) return new() { "One of the disks is null." };

        void Cmp(string name, string? x, string? y)
        {
            if (!string.Equals(x ?? "", y ?? "", StringComparison.OrdinalIgnoreCase))
                diffs.Add($"{name}: '{x}' → '{y}'");
        }

        if (selected.Number != current.Number) diffs.Add($"Number: {selected.Number} → {current.Number}");
        Cmp("Model", selected.Model, current.Model);
        Cmp("Serial", selected.SerialNumber, current.SerialNumber);
        Cmp("UniqueId", selected.UniqueId, current.UniqueId);
        Cmp("BusType", selected.BusType, current.BusType);
        if (selected.SizeBytes != current.SizeBytes) diffs.Add($"Size: {selected.SizeBytes} → {current.SizeBytes}");

        return diffs;
    }
}
