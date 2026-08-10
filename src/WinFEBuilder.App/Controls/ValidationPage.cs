using System.Drawing;
using WinFEBuilder.App.ViewModels;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;

namespace WinFEBuilder.App.Controls;

/// <summary>
/// Validation page: a streamlined, guided form to RECORD manual test results (never fabricated).
/// The fields shown here are exactly the fields stored on <see cref="ValidationRecord"/> - the record
/// carries no hidden/unused inputs.
/// </summary>
public sealed class ValidationPage : UserControl, INavigationAware
{
    private readonly ValidationViewModel _vm;
    private readonly ILogService _log;

    private readonly TextBox _buildId = new();
    private readonly TextBox _usbSerial = new();
    private readonly TextBox _examiner = new();
    private readonly DateTimePicker _testDate = new();
    private readonly Dictionary<string, ComboBox> _checks = new();

    private TableLayoutPanel _grid = null!;
    private int _row;

    public ValidationPage(ValidationViewModel vm, ILogService log)
    {
        _vm = vm;
        _log = log;
        Dock = DockStyle.Fill;
        BackColor = UiTheme.Background;
        Padding = new Padding(16);

        Controls.Add(BuildBody());
        Controls.Add(BuildHeader());
    }

    /// <summary>
    /// Called each time the page is shown. Keeps the Build ID in sync with the most recent build so
    /// the saved validation record is tied to the build the report will be generated from - the
    /// operator does not type it, which removes typos and the build/validation mismatch they cause.
    /// </summary>
    public void OnNavigatedTo()
    {
        var latest = _vm.LatestBuildId();
        if (!string.IsNullOrWhiteSpace(latest))
            _buildId.Text = latest;

        // Same reasoning as the Build ID: the serial identifies the medium this report attests to,
        // and the app already captured it during USB creation. Only pre-fill when the operator has
        // not typed something else — validating an older USB must stay possible.
        if (string.IsNullOrWhiteSpace(_usbSerial.Text))
        {
            var serial = _vm.LatestUsbSerial();
            if (!string.IsNullOrWhiteSpace(serial)) _usbSerial.Text = serial;
        }
    }

    private Control BuildHeader()
    {
        var p = new Panel { Dock = DockStyle.Top, Height = 56 };
        p.Controls.Add(new Label { Text = "Validation", Font = UiTheme.Heading, ForeColor = UiTheme.TextPrimary, AutoSize = true, Location = new Point(0, 0) });
        p.Controls.Add(new Label
        {
            Text = "Record the results of your manual tests. These are human-entered — the app never marks them automatically.",
            Font = UiTheme.Body, ForeColor = UiTheme.TextSecondary, AutoSize = true, Location = new Point(0, 30)
        });
        return p;
    }

    private Control BuildBody()
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        _grid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            Padding = new Padding(0, 8, 0, 8),
            Location = new Point(0, 0)
        };
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));

        Section("Build Information");
        AddText("Build ID:", _buildId);
        AddText("USB serial number:", _usbSerial);

        Section("Boot tests");
        _checks["BootedUefi"] = AddCheck("Booted successfully in UEFI");
        _checks["BootedLegacyBios"] = AddCheck("Booted successfully in Legacy BIOS");

        Section("Write-protection (forensic soundness)");
        _checks["InternalSourceOfflineOrReadOnly"] = AddCheck("Internal source disk remained offline / read-only");
        _checks["TestSourceHashMatchedBeforeAfter"] = AddCheck("Test source disk hash matched before and after boot");

        Section("Validation details");
        AddText("Examiner:", _examiner);

        _testDate.Width = 150;
        _testDate.Font = UiTheme.Body;
        _testDate.Format = DateTimePickerFormat.Short;
        _testDate.ShowCheckBox = true;
        _testDate.Checked = true;
        AddControl("Test date:", _testDate);

        _checks["UsbDestinationDetected"] = AddCheck("USB destination detected");

        var generate = new Button
        {
            Text = "Generate Report",
            Font = UiTheme.Subheading,
            BackColor = UiTheme.Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            AutoSize = true,
            Padding = new Padding(14, 6, 14, 6),
            Margin = new Padding(0, 14, 0, 6)
        };
        generate.FlatAppearance.BorderSize = 0;
        generate.Click += OnGenerateReport;
        AddFullWidth(generate);

        scroll.Controls.Add(_grid);
        return scroll;
    }

    // --- layout helpers (explicit row management so nothing overlaps or leaves gaps) ---

    private void Section(string text)
    {
        var l = new Label
        {
            Text = text, Font = UiTheme.Subheading, ForeColor = UiTheme.TextPrimary,
            AutoSize = true, Margin = new Padding(0, 14, 0, 4)
        };
        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _grid.Controls.Add(l, 0, _row);
        _grid.SetColumnSpan(l, 2);
        _row++;
    }

    private void AddControl(string label, Control control)
    {
        var l = new Label
        {
            Text = label, AutoSize = true, Font = UiTheme.Body, ForeColor = UiTheme.TextSecondary,
            Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 3)
        };
        control.Margin = new Padding(0, 3, 0, 3);
        control.Anchor = AnchorStyles.Left;
        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _grid.Controls.Add(l, 0, _row);
        _grid.Controls.Add(control, 1, _row);
        _row++;
    }

    private void AddFullWidth(Control control)
    {
        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _grid.Controls.Add(control, 0, _row);
        _grid.SetColumnSpan(control, 2);
        _row++;
    }

    private void AddText(string label, TextBox box)
    {
        box.Width = 330; box.Font = UiTheme.Body;
        AddControl(label, box);
    }

    private ComboBox AddCheck(string label)
    {
        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        cb.Items.AddRange(new object[] { "Not Tested", "Pass", "Fail", "N/A" });
        cb.SelectedIndex = 0;
        cb.AccessibleName = label;
        AddControl(label, cb);
        return cb;
    }

    private static ManualCheck ToCheck(ComboBox cb) => cb.SelectedIndex switch
    {
        1 => ManualCheck.Pass,
        2 => ManualCheck.Fail,
        3 => ManualCheck.NotApplicable,
        _ => ManualCheck.NotTested
    };

    /// <summary>
    /// Gates report generation on the things that make a validation report meaningful.
    /// </summary>
    /// <remarks>
    /// A validation report is a signed human attestation. Two states make one misleading rather than
    /// merely incomplete: no examiner (an official-looking document attributed to nobody), and every
    /// check left at "Not Tested" (a document that certifies nothing while appearing authoritative).
    /// The first is refused outright; the second is confirmable, because recording a partial run
    /// mid-validation is legitimate.
    /// </remarks>
    private bool ConfirmReportIsMeaningful()
    {
        if (string.IsNullOrWhiteSpace(_examiner.Text))
        {
            MessageBox.Show(
                "Enter the examiner's name before generating the report.\n\n"
                + "A validation report is an attestation that these tests were performed, so it cannot "
                + "be issued without recording who performed them.",
                "Examiner required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _examiner.Focus();
            return false;
        }

        var untested = _checks.Values.Count(cb => ToCheck(cb) == ManualCheck.NotTested);
        if (untested == _checks.Count)
        {
            var proceed = MessageBox.Show(
                $"All {_checks.Count} checks are still set to \"Not Tested\".\n\n"
                + "The report will record that nothing has been verified — including write protection. "
                + "Generate it anyway?",
                "Nothing has been tested", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (proceed != DialogResult.Yes) return false;
        }

        return true;
    }

    private void OnGenerateReport(object? sender, EventArgs e)
    {
        if (!ConfirmReportIsMeaningful()) return;

        // Every field on the record is set from the form below (the record has no other inputs).
        var record = new ValidationRecord
        {
            BuildReference = NullIfEmpty(_buildId.Text),
            UsbSerialNumber = NullIfEmpty(_usbSerial.Text),
            BootedUefi = ToCheck(_checks["BootedUefi"]),
            BootedLegacyBios = ToCheck(_checks["BootedLegacyBios"]),
            InternalSourceOfflineOrReadOnly = ToCheck(_checks["InternalSourceOfflineOrReadOnly"]),
            TestSourceHashMatchedBeforeAfter = ToCheck(_checks["TestSourceHashMatchedBeforeAfter"]),
            UsbDestinationDetected = ToCheck(_checks["UsbDestinationDetected"]),
            ExaminerName = NullIfEmpty(_examiner.Text),
            TestDate = _testDate.Checked
                ? new DateTimeOffset(
                    _testDate.Value,
                    TimeZoneInfo.Local.GetUtcOffset(_testDate.Value))
                : null
        };

        try
        {
            // One step: build the HTML report for the current build from what was typed, then open it.
            var html = _vm.GenerateReport(record);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = html,
                UseShellExecute = true
            });
            MessageBox.Show($"Report generated:\n{html}\n\nThe report is opening now.",
                "Report", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _log.Error("Validation", "Failed to generate report.", ex);
            MessageBox.Show(ex.Message, "Report generation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
