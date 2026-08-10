using System.Text.RegularExpressions;
using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Validation;

/// <summary>
/// Parses the text output of <c>dism /Get-WimInfo</c> (run with /English). Pure and IO-free
/// so it can be unit tested against captured DISM output.
/// </summary>
public static class DismOutputParser
{
    /// <summary>Parse the summary form: <c>dism /English /Get-WimInfo /WimFile:"x.wim"</c>.</summary>
    public static List<WimImage> ParseImageList(string dismOutput)
    {
        var images = new List<WimImage>();
        if (string.IsNullOrWhiteSpace(dismOutput)) return images;

        int? index = null;
        string? name = null, description = null, arch = null;
        long? size = null;

        void Flush()
        {
            if (index.HasValue)
                images.Add(new WimImage
                {
                    Index = index.Value,
                    Name = name,
                    Description = description,
                    Architecture = arch,
                    SizeBytes = size
                });
            index = null; name = description = arch = null; size = null;
        }

        foreach (var raw in dismOutput.Split('\n'))
        {
            var line = raw.Trim();
            var (key, value) = SplitKeyValue(line);
            if (key is null) continue;

            switch (key.ToLowerInvariant())
            {
                case "index":
                    // A new index block begins — flush the previous one.
                    Flush();
                    if (int.TryParse(value, out var idx)) index = idx;
                    break;
                case "name":
                    name = value;
                    break;
                case "description":
                    description = value;
                    break;
                case "architecture":
                    arch = value;
                    break;
                case "size":
                    size = ParseSize(value);
                    break;
            }
        }
        Flush();
        return images;
    }

    /// <summary>Extract the architecture from a per-index detail output, if present.</summary>
    public static string? ParseArchitecture(string dismOutput)
    {
        if (string.IsNullOrWhiteSpace(dismOutput)) return null;
        foreach (var raw in dismOutput.Split('\n'))
        {
            var (key, value) = SplitKeyValue(raw.Trim());
            if (key is not null && key.Equals("Architecture", StringComparison.OrdinalIgnoreCase))
                return value;
        }
        return null;
    }

    /// <summary>DISM reports success with a trailing "The operation completed successfully." line.</summary>
    public static bool IndicatesSuccess(string dismOutput) =>
        !string.IsNullOrEmpty(dismOutput) &&
        dismOutput.Contains("completed successfully", StringComparison.OrdinalIgnoreCase);

    private static (string? key, string value) SplitKeyValue(string line)
    {
        // DISM uses "Key : Value" (note the spaces around the colon).
        var m = Regex.Match(line, @"^(?<k>[A-Za-z][A-Za-z /]*?)\s*:\s*(?<v>.*)$");
        if (!m.Success) return (null, string.Empty);
        return (m.Groups["k"].Value.Trim(), m.Groups["v"].Value.Trim());
    }

    private static long? ParseSize(string value)
    {
        // e.g. "1,505,290,858 bytes"
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out var n) ? n : null;
    }
}
