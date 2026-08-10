using System.Drawing;
using WinFEBuilder.Core.Models;

namespace WinFEBuilder.App.Controls;

/// <summary>Modal dialog showing the full details + recommended action for an audit item.</summary>
public sealed class DetailsDialog : Form
{
    public DetailsDialog(AuditItem item)
    {
        Text = item.Name;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(520, 360);
        Size = new Size(640, 460);
        Font = UiTheme.Body;
        BackColor = UiTheme.Surface;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(16)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new Label
        {
            Text = item.Name,
            Font = UiTheme.Heading,
            ForeColor = UiTheme.TextPrimary,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };

        var status = new Label
        {
            Text = "Status: " + UiTheme.StatusText(item.Status),
            Font = UiTheme.Subheading,
            ForeColor = UiTheme.StatusColor(item.Status),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };

        var details = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = UiTheme.Mono,
            BackColor = Color.FromArgb(249, 250, 251),
            Text = BuildBody(item)
        };

        var close = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 8, 0, 0),
            Padding = new Padding(12, 4, 12, 4)
        };
        close.Click += (_, _) => Close();

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(status, 0, 1);
        layout.Controls.Add(details, 0, 2);
        layout.Controls.Add(close, 0, 3);

        Controls.Add(layout);
        AcceptButton = close;
        CancelButton = close;
    }

    private static string BuildBody(AuditItem item)
    {
        var text = item.Details ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(item.RecommendedAction))
            text += Environment.NewLine + Environment.NewLine + "Recommended action:" + Environment.NewLine + item.RecommendedAction;
        return text;
    }
}
