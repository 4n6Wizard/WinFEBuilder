using System.Drawing;
using WinFEBuilder.App.ViewModels;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;

namespace WinFEBuilder.App.Controls;

/// <summary>Dashboard page: environment audit status cards + live log.</summary>
public sealed class DashboardPage : UserControl
{
    private readonly DashboardViewModel _vm;
    private readonly ILogService _log;

    private readonly Button _runButton = new();
    private readonly Button _cancelButton = new();
    private readonly ProgressBar _progress = new();
    private readonly Label _overall = new();
    private readonly FlowLayoutPanel _cards = new();
    private readonly LiveLogPanel _liveLog;

    private CancellationTokenSource? _cts;

    public DashboardPage(DashboardViewModel vm, ILogService log)
    {
        _vm = vm;
        _log = log;
        _liveLog = new LiveLogPanel(log);

        Dock = DockStyle.Fill;
        BackColor = UiTheme.Background;
        Padding = new Padding(16);

        Controls.Add(BuildBody());
        Controls.Add(BuildHeader());
    }

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 108, BackColor = UiTheme.Background };

        var title = new Label
        {
            Text = "Dashboard",
            Font = UiTheme.Heading,
            ForeColor = UiTheme.TextPrimary,
            AutoSize = true,
            Location = new Point(0, 0)
        };

        var subtitle = new Label
        {
            Text = "Environment audit — verifies the tools and prerequisites required to build WinFE media.",
            Font = UiTheme.Body,
            ForeColor = UiTheme.TextSecondary,
            AutoSize = true,
            Location = new Point(0, 28)
        };

        _runButton.Text = "Run Environment Audit";
        _runButton.Font = UiTheme.Subheading;
        _runButton.BackColor = UiTheme.Accent;
        _runButton.ForeColor = Color.White;
        _runButton.FlatStyle = FlatStyle.Flat;
        _runButton.FlatAppearance.BorderSize = 0;
        _runButton.AutoSize = true;
        _runButton.Padding = new Padding(14, 8, 14, 8);
        _runButton.Location = new Point(0, 56);
        _runButton.Click += async (_, _) => await RunAuditAsync();

        _cancelButton.Text = "Cancel";
        _cancelButton.Font = UiTheme.Body;
        _cancelButton.AutoSize = true;
        _cancelButton.Enabled = false;
        _cancelButton.Padding = new Padding(10, 6, 10, 6);
        _cancelButton.Location = new Point(210, 58);
        _cancelButton.Click += (_, _) => _cts?.Cancel();

        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = 0;
        _progress.Visible = false;
        _progress.Width = 220;
        _progress.Height = 18;
        _progress.Location = new Point(300, 64);

        _overall.Text = "Overall: not yet audited";
        _overall.Font = UiTheme.Subheading;
        _overall.ForeColor = UiTheme.TextSecondary;
        _overall.AutoSize = true;
        _overall.Location = new Point(540, 64);

        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        header.Controls.Add(_runButton);
        header.Controls.Add(_cancelButton);
        header.Controls.Add(_progress);
        header.Controls.Add(_overall);
        return header;
    }

    private Control BuildBody()
    {
        // Deterministic layout: cards fill the top, live log gets a fixed band at the bottom.
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));

        _cards.Dock = DockStyle.Fill;
        _cards.AutoScroll = true;
        _cards.WrapContents = true;
        _cards.FlowDirection = FlowDirection.LeftToRight;
        _cards.BackColor = UiTheme.Background;
        _cards.Padding = new Padding(0, 8, 0, 0);

        _liveLog.Dock = DockStyle.Fill;
        _liveLog.Margin = new Padding(0, 8, 0, 0);

        root.Controls.Add(_cards, 0, 0);
        root.Controls.Add(_liveLog, 0, 1);
        return root;
    }

    private async Task RunAuditAsync()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        SetBusy(true);

        try
        {
            var result = await _vm.RunAuditAsync(_cts.Token);
            RenderResult(result);
        }
        catch (OperationCanceledException)
        {
            _overall.Text = "Overall: audit canceled";
            _overall.ForeColor = UiTheme.Warning;
        }
        catch (Exception ex)
        {
            _log.Error("Dashboard", "Audit failed.", ex);
            MessageBox.Show(ex.Message, "Audit failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RenderResult(EnvironmentAuditResult result)
    {
        _cards.SuspendLayout();
        _cards.Controls.Clear();
        foreach (var item in result.Items)
        {
            var card = new StatusCard();
            card.Bind(item);
            card.DetailsRequested += (_, it) => new DetailsDialog(it).ShowDialog(FindForm());
            _cards.Controls.Add(card);
        }
        _cards.ResumeLayout();

        _overall.Text = "Overall: " + UiTheme.StatusText(result.Overall);
        _overall.ForeColor = UiTheme.StatusColor(result.Overall);
    }

    private void SetBusy(bool busy)
    {
        _runButton.Enabled = !busy;
        _cancelButton.Enabled = busy;
        _progress.Visible = busy;
        _progress.MarqueeAnimationSpeed = busy ? 30 : 0;
        if (busy)
        {
            _overall.Text = "Overall: auditing…";
            _overall.ForeColor = UiTheme.TextSecondary;
        }
    }

    /// <summary>Auto-run the audit when the dashboard first becomes visible.</summary>
    public async void RunInitialAudit()
    {
        if (_vm.LastResult is null)
            await RunAuditAsync();
    }
}
