namespace NitroPulse;

internal sealed class UpdateDialog : Form
{
    public UpdateDialog(Action<ReadyUpdate> onReady)
    {
        Text = "NitroPulse · Actualizaciones";
        ClientSize = new Size(560, 320);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        var page = new UpdatesPage(onReady) { Dock = DockStyle.Fill };
        var close = new Button { Text = "Cerrar", Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.Cancel };
        Theme.StyleButton(close);
        LucideIcons.Apply(close, "x");
        Controls.Add(page);
        Controls.Add(close);
        CancelButton = close;
        HandleCreated += (_, _) => WindowChrome.Apply(this);
        ApplyTheme();
        Theme.Changed += ApplyTheme;
        FormClosed += (_, _) => Theme.Changed -= ApplyTheme;

        void ApplyTheme()
        {
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.FontBody;
            WindowChrome.Apply(this);
            Theme.StyleButton(close);
            LucideIcons.Apply(close, "x");
            page.ApplyTheme();
        }
    }
}
