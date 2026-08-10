namespace WinFEBuilder.App.Controls;

/// <summary>
/// Minimal single-line text prompt. WinForms has no built-in InputBox, and the one place that needs
/// one (a destination path inside the image) does not justify a dedicated form.
/// </summary>
internal static class Prompt
{
    /// <summary>Returns the entered text, or null if the user cancelled.</summary>
    public static string? Show(IWin32Window? owner, string title, string label, string initialValue = "")
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(520, 130),
            BackColor = UiTheme.Background
        };

        var prompt = new Label
        {
            Text = label,
            Left = 14, Top = 14, Width = 490, Height = 20,
            Font = UiTheme.Body,
            ForeColor = UiTheme.TextPrimary
        };

        var box = new TextBox
        {
            Text = initialValue,
            Left = 14, Top = 40, Width = 490,
            Font = UiTheme.Body
        };
        box.SelectAll();

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 330, Top = 78, Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 420, Top = 78, Width = 84 };

        form.Controls.AddRange(new Control[] { prompt, box, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        return form.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : null;
    }
}
