using System.Drawing;

namespace WinFEBuilder.App.Controls;

/// <summary>
/// Informational page for milestones not yet implemented. This is NOT a fake control:
/// it contains no buttons that pretend to do work — only a clear status message.
/// </summary>
public sealed class PlaceholderPage : UserControl
{
    public PlaceholderPage(string title, string milestone, string description)
    {
        Dock = DockStyle.Fill;
        BackColor = UiTheme.Background;
        Padding = new Padding(24);

        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };

        stack.Controls.Add(new Label
        {
            Text = title,
            Font = UiTheme.Heading,
            ForeColor = UiTheme.TextPrimary,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        });

        stack.Controls.Add(new Label
        {
            Text = $"Planned for {milestone}.",
            Font = UiTheme.Subheading,
            ForeColor = UiTheme.Warning,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12)
        });

        stack.Controls.Add(new Label
        {
            Text = description,
            Font = UiTheme.Body,
            ForeColor = UiTheme.TextSecondary,
            AutoSize = true,
            MaximumSize = new Size(760, 0)
        });

        Controls.Add(stack);
    }
}
