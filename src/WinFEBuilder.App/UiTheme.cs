using System.Drawing;
using WinFEBuilder.Core.Models;

namespace WinFEBuilder.App;

/// <summary>Neutral professional palette and status color mapping used across the UI.</summary>
internal static class UiTheme
{
    public static readonly Color Background = Color.FromArgb(243, 244, 246);
    public static readonly Color Surface = Color.White;
    public static readonly Color NavBackground = Color.FromArgb(31, 41, 55);
    public static readonly Color NavForeground = Color.FromArgb(229, 231, 235);
    public static readonly Color NavSelected = Color.FromArgb(55, 65, 81);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);
    public static readonly Color TextPrimary = Color.FromArgb(17, 24, 39);
    public static readonly Color TextSecondary = Color.FromArgb(75, 85, 99);
    public static readonly Color Border = Color.FromArgb(209, 213, 219);

    public static readonly Color Pass = Color.FromArgb(22, 128, 61);
    public static readonly Color Warning = Color.FromArgb(180, 120, 10);
    public static readonly Color Fail = Color.FromArgb(185, 28, 28);
    public static readonly Color Neutral = Color.FromArgb(107, 114, 128);

    public static Color StatusColor(CheckStatus status) => status switch
    {
        CheckStatus.Pass => Pass,
        CheckStatus.Warning => Warning,
        CheckStatus.Fail => Fail,
        _ => Neutral
    };

    public static string StatusText(CheckStatus status) => status switch
    {
        CheckStatus.Pass => "PASS",
        CheckStatus.Warning => "WARNING",
        CheckStatus.Fail => "FAIL",
        _ => "NOT CONFIGURED"
    };

    // Cached, shared font instances (application lifetime). Previously these properties allocated a
    // new Font on every access and were never disposed, churning GDI font handles. Sharing a single
    // immutable Font across controls is safe — controls do not dispose fonts they are merely assigned.
    public static Font Heading { get; } = new("Segoe UI Semibold", 14f, FontStyle.Regular);
    public static Font Subheading { get; } = new("Segoe UI Semibold", 11f, FontStyle.Regular);
    public static Font Body { get; } = new("Segoe UI", 9.5f, FontStyle.Regular);
    public static Font Mono { get; } = new("Consolas", 9.5f, FontStyle.Regular);
}
