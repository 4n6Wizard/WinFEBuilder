using System.Drawing;
using Microsoft.Extensions.DependencyInjection;
using WinFEBuilder.App.Controls;
using WinFEBuilder.App.ViewModels;
using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Services;

namespace WinFEBuilder.App.Forms;

public sealed class MainForm : Form
{
    private readonly IServiceProvider _sp;
    private readonly ILogService _log;

    private readonly Panel _nav = new();
    private readonly Panel _content = new();
    private readonly Dictionary<string, Control> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Button> _navButtons = new();

    private DashboardPage? _dashboard;

    private static readonly (string Key, string Label)[] NavItems =
    {
        ("dashboard", "Dashboard"),
        ("framework", "WinFE Source"),
        ("tools", "Tools and Drivers"),
        ("wallpaper", "Wallpaper"),
        ("build", "Build"),
        ("usb", "USB"),
        ("validation", "Validation"),
        ("settings", "Settings"),
    };

    public MainForm(IServiceProvider sp, ILogService log)
    {
        _sp = sp;
        _log = log;

        Text = "WinFE Builder";
        MinimumSize = new Size(1024, 680);
        // Tools and Drivers is the tallest page. Open as large as the screen comfortably allows so it
        // fits without scrolling, but never larger than the working area — a fixed 950px tall window is
        // taller than a 768p laptop screen, which pushes controls off the bottom edge.
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        Size = new Size(Math.Min(1280, work.Width - 40), Math.Min(980, work.Height - 40));
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiTheme.Background;
        Font = UiTheme.Body;
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildNav();
        BuildContent();

        Controls.Add(_content);
        Controls.Add(_nav);

        Navigate("dashboard");
        Shown += (_, _) => _dashboard?.RunInitialAudit();
    }

    private void BuildNav()
    {
        _nav.Dock = DockStyle.Left;
        _nav.Width = 220;
        _nav.BackColor = UiTheme.NavBackground;
        _nav.Padding = new Padding(0, 12, 0, 0);

        var brand = new Label
        {
            Text = "WinFE Builder",
            Dock = DockStyle.Top,
            Height = 56,
            Font = new Font("Segoe UI Semibold", 13f),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var version = new Label
        {
            Text = "v1.0 - M1-M5 complete",
            Dock = DockStyle.Bottom,
            Height = 28,
            Font = UiTheme.Body,
            ForeColor = UiTheme.NavForeground,
            TextAlign = ContentAlignment.MiddleCenter
        };

        // Build buttons bottom-up so Dock=Top preserves declared order.
        foreach (var (key, label) in NavItems.Reverse())
        {
            var btn = new Button
            {
                Text = "   " + label,
                Tag = key,
                Dock = DockStyle.Top,
                Height = 44,
                FlatStyle = FlatStyle.Flat,
                ForeColor = UiTheme.NavForeground,
                BackColor = UiTheme.NavBackground,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UiTheme.Subheading,
                Cursor = Cursors.Hand,
                TabStop = true
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = UiTheme.NavSelected;
            btn.AccessibleName = label;
            btn.Click += (_, _) => Navigate(key);
            _navButtons.Add(btn);
            _nav.Controls.Add(btn);
        }

        _nav.Controls.Add(version);
        _nav.Controls.Add(brand);
    }

    private void BuildContent()
    {
        _content.Dock = DockStyle.Fill;
        _content.BackColor = UiTheme.Background;
        _content.Padding = new Padding(0);
    }

    private void Navigate(string key)
    {
        var page = GetOrCreatePage(key);
        _content.SuspendLayout();
        _content.Controls.Clear();
        _content.Controls.Add(page);
        _content.ResumeLayout();

        // Cached pages (kept in _pages) refresh here so data saved on other pages is picked up.
        (page as INavigationAware)?.OnNavigatedTo();

        foreach (var b in _navButtons)
        {
            var selected = string.Equals((string)b.Tag!, key, StringComparison.OrdinalIgnoreCase);
            b.BackColor = selected ? UiTheme.NavSelected : UiTheme.NavBackground;
            b.ForeColor = selected ? Color.White : UiTheme.NavForeground;
        }

        _log.Debug("Nav", $"Navigated to {key}.");
    }

    private Control GetOrCreatePage(string key)
    {
        if (_pages.TryGetValue(key, out var existing))
            return existing;

        Control page = key switch
        {
            "dashboard" => _dashboard = new DashboardPage(
                new DashboardViewModel(_sp.GetRequiredService<IEnvironmentService>()),
                _log),

            "framework" => new FrameworkPage(
                new FrameworkViewModel(
                    _sp.GetRequiredService<IFrameworkService>(),
                    _sp.GetRequiredService<ISettingsService>()),
                _log),

            "tools" => new ToolsAndDriversPage(
                new ToolsAndDriversViewModel(
                    _sp.GetRequiredService<IToolService>(),
                    _sp.GetRequiredService<IDriverService>(),
                    _sp.GetRequiredService<ISettingsService>(),
                    _sp.GetRequiredService<IAdkDetectionService>(),
                    _sp.GetRequiredService<IImageContentService>()),
                _log),

            "wallpaper" => new WallpaperPage(
                new WallpaperViewModel(
                    _sp.GetRequiredService<IWallpaperService>(),
                    _sp.GetRequiredService<ISettingsService>()),
                _log),

            "build" => new BuildPage(
                new BuildViewModel(
                    _sp.GetRequiredService<IBuildService>(),
                    _sp.GetRequiredService<IFrameworkService>(),
                    _sp.GetRequiredService<ISettingsService>()),
                _log),

            "usb" => new UsbPage(
                new UsbViewModel(
                    _sp.GetRequiredService<IDiskService>(),
                    _sp.GetRequiredService<ISettingsService>()),
                _log),

            "validation" => new ValidationPage(
                new ValidationViewModel(
                    _sp.GetRequiredService<Core.Reports.IReportService>()),
                _log),

            "settings" => BuildSettingsPage(),

            _ => new PlaceholderPage(key, "Later milestone", "Not yet implemented.")
        };

        _pages[key] = page;
        return page;
    }

    private Control BuildSettingsPage()
    {
        // Minimal read-only settings summary for Milestone 1 (full editor arrives later).
        var settings = _sp.GetRequiredService<ISettingsService>();
        var paths = _sp.GetRequiredService<AppPaths>();
        var s = settings.Settings;

        var text =
            $"Settings file: {settings.SettingsFilePath}\r\n\r\n" +
            $"Workspace root:  {s.WorkspaceRoot}\r\n" +
            $"Output root:     {s.OutputRoot}\r\n" +
            $"Log directory:   {paths.LogDir}\r\n" +
            $"This session:    {paths.SessionLogDir}\r\n" +
            $"Report root:     {s.ReportRoot}\r\n" +
            $"Min free space:  {s.MinimumFreeSpaceGb:0.#} GB\r\n" +
            $"USB writes:      REAL — the selected disk is erased\r\n" +
            $"PowerShell:      {s.PreferredPowerShell}\r\n" +
            $"Operator:        {s.OperatorName ?? "(not set)"}\r\n" +
            $"Organization:    {s.OrganizationName ?? "(not set)"}\r\n\r\n" +
            "Edit config/settings.json to change these values. A full settings editor is planned for a later milestone.\r\n\r\n" +
            BuildProfilesSummary();

        var page = new UserControl { Dock = DockStyle.Fill, BackColor = UiTheme.Background, Padding = new Padding(16) };
        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            Font = UiTheme.Mono,
            BackColor = UiTheme.Surface,
            ScrollBars = ScrollBars.Vertical,
            Text = text
        };
        var header = new Label { Text = "Settings", Dock = DockStyle.Top, Height = 36, Font = UiTheme.Heading, ForeColor = UiTheme.TextPrimary };
        page.Controls.Add(box);
        page.Controls.Add(header);
        return page;
    }

    private string BuildProfilesSummary()
    {
        try
        {
            var profiles = _sp.GetRequiredService<IProfileService>();
            var list = profiles.List();
            var lines = new List<string> { $"Build profiles ({profiles.ProfilesFilePath}):" };
            foreach (var p in list)
                lines.Add($"  - {p.Name}  [USB: {p.UsbLayout}]");
            lines.Add("(Profiles store framework/workspace/output/tools/drivers/wallpaper/USB layout - never disk numbers.)");
            return string.Join("\r\n", lines);
        }
        catch
        {
            return "Build profiles: (unavailable)";
        }
    }
}
