using System.Text;
using System.Text.RegularExpressions;

namespace WinFEBuilder.Core.Validation;

/// <summary>Registry verb used by a WinFE build script.</summary>
public enum WinFeRegistryVerb
{
    Load,
    Unload,
    Add,
    Delete
}

/// <summary>One registry operation lifted from a WinFE build batch file.</summary>
public sealed class WinFeRegistryOperation
{
    public required WinFeRegistryVerb Verb { get; init; }

    /// <summary>Temporary hive key the script mounts to, e.g. <c>HKLM\FE_SYSTEM</c>. Load/Unload only.</summary>
    public string? HiveKey { get; init; }

    /// <summary>
    /// Hive file path relative to the image root, e.g. <c>Windows\System32\Config\SYSTEM</c>.
    /// Load only. Rebased onto the real mount directory at replay time.
    /// </summary>
    public string? HiveFileRelativePath { get; init; }

    /// <summary>Arguments for reg.exe after the verb. Add/Delete only.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    /// <summary>The original script line, for logging.</summary>
    public required string RawLine { get; init; }
}

/// <summary>
/// Extracts the offline-registry patches a WinFE build script applies to boot.wim — write
/// protection (SanPolicy, NoAutoMount), shell namespace registration, and so on.
/// </summary>
/// <remarks>
/// <para>
/// WinFE requires these settings to be written LAST, after every package and driver is installed.
/// DISM component servicing re-applies each package's own registry state, which silently reverts
/// hand-injected values a build script wrote earlier. Because the builder installs WinPE optional
/// components after running the framework's batch, the batch's settings must be replayed afterward
/// or the finished image can lose its write protection.
/// </para>
/// <para>
/// Settings are read back out of the framework's own script rather than hardcoded, so any WinFE
/// framework works and the replayed values can never drift from what that framework intended.
/// </para>
/// </remarks>
public static class WinFeRegistryPatchParser
{
    private static readonly Regex RegCommand = new(
        @"^\s*reg(?:\.exe)?\s+(load|unload|add|delete)\s+(\S.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Hive files always live at <image>\Windows\System32\Config\<HIVE>. Anchoring on that lets us
    // discard whatever mount directory the script used and rebase onto our own.
    private static readonly Regex HiveFileTail = new(
        @"(Windows\\System32\\Config\\[A-Za-z0-9_]+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parses the registry operations from <paramref name="batchText"/>.
    /// </summary>
    /// <remarks>
    /// Dual-architecture scripts repeat an identical block per architecture (once for x64, once for
    /// x86). Only the first complete block is returned — it is replayed against each boot.wim
    /// individually, so returning both would just apply everything twice.
    /// </remarks>
    public static List<WinFeRegistryOperation> Parse(string batchText)
    {
        var ops = new List<WinFeRegistryOperation>();
        if (string.IsNullOrWhiteSpace(batchText)) return ops;

        var sawUnload = false;

        foreach (var rawLine in batchText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            // Skip batch comments (":: comment" and "rem comment").
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("::", StringComparison.Ordinal) ||
                trimmed.StartsWith("rem ", StringComparison.OrdinalIgnoreCase))
                continue;

            var m = RegCommand.Match(line);
            if (!m.Success) continue;

            var verb = m.Groups[1].Value.ToLowerInvariant() switch
            {
                "load" => WinFeRegistryVerb.Load,
                "unload" => WinFeRegistryVerb.Unload,
                "add" => WinFeRegistryVerb.Add,
                _ => WinFeRegistryVerb.Delete
            };

            // A Load after the first block's Unload means the next architecture's block has begun.
            if (verb == WinFeRegistryVerb.Load && sawUnload) break;

            var tokens = Tokenize(m.Groups[2].Value);
            if (tokens.Count == 0) continue;

            switch (verb)
            {
                case WinFeRegistryVerb.Load:
                {
                    if (tokens.Count < 2) continue;
                    var tail = HiveFileTail.Match(tokens[1]);
                    if (!tail.Success) continue;   // Unrecognised hive location; skip rather than guess.
                    ops.Add(new WinFeRegistryOperation
                    {
                        Verb = verb,
                        HiveKey = tokens[0],
                        HiveFileRelativePath = tail.Groups[1].Value,
                        RawLine = line
                    });
                    break;
                }

                case WinFeRegistryVerb.Unload:
                    sawUnload = true;
                    ops.Add(new WinFeRegistryOperation
                    {
                        Verb = verb,
                        HiveKey = tokens[0],
                        RawLine = line
                    });
                    break;

                default:
                    ops.Add(new WinFeRegistryOperation
                    {
                        Verb = verb,
                        Arguments = tokens,
                        RawLine = line
                    });
                    break;
            }
        }

        return ops;
    }

    /// <summary>
    /// Splits a command tail into arguments, honouring double quotes and unescaping batch's
    /// doubled percent signs (<c>%%systemroot%%</c> is a literal <c>%systemroot%</c> once cmd has
    /// processed it, and we hand arguments to reg.exe directly rather than through cmd).
    /// </summary>
    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var started = false;

        foreach (var ch in text)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                started = true;
                continue;
            }

            if (!inQuotes && (ch == ' ' || ch == '\t'))
            {
                if (started)
                {
                    tokens.Add(current.ToString().Replace("%%", "%"));
                    current.Clear();
                    started = false;
                }
                continue;
            }

            current.Append(ch);
            started = true;
        }

        if (started) tokens.Add(current.ToString().Replace("%%", "%"));
        return tokens;
    }
}
