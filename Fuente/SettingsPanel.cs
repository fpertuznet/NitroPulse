namespace NitroPulse;

internal enum SettingsSection { Export, Sensors, Updates, Themes, Privacy }

/// <summary>Configuración host: left nav + one visible section at a time.</summary>
internal sealed class SettingsPanel : UserControl
{
    private readonly FlowLayoutPanel _nav = new()
    {
        Dock = DockStyle.Left, Width = 220, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, Padding = new Padding(8, 12, 8, 12)
    };
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly List<(SettingsSection Section, Button Nav, Control Page)> _items = new();
    private readonly TextBox _sensorView;
    private Label _exportStatus = null!;
    private readonly List<Button> _themeButtons = new();
    private readonly Func<Task> _exportAsync;
    private readonly Func<string> _sensorReport;
    private readonly Action<string> _saveSensorReport;
    private SettingsSection _selected = SettingsSection.Export;

    internal SettingsPanel(Func<Task> exportAsync, Func<string> sensorReport, Action<string> saveSensorReport, Action<ReadyUpdate> onReady)
    {
        _exportAsync = exportAsync;
        _sensorReport = sensorReport;
        _saveSensorReport = saveSensorReport;
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        Controls.Add(_content);
        Controls.Add(_nav);

        AddSection(SettingsSection.Export, "Exportar ZIP", "file-down", BuildExportPage());
        _sensorView = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            WordWrap = false, BorderStyle = BorderStyle.None, Font = Theme.FontMono
        };
        AddSection(SettingsSection.Sensors, "Sensores", "activity", BuildSensorsPage());
        AddSection(SettingsSection.Updates, "Actualizaciones", "refresh-cw", new UpdatesPage(onReady));
        AddSection(SettingsSection.Themes, "Temas", "monitor", BuildThemesPage());
        AddSection(SettingsSection.Privacy, "Privacidad y licencias", "file-text", new NoticesPanel());

        SelectSection(SettingsSection.Export);
        Theme.Changed += ApplyTheme;
        Disposed += (_, _) => Theme.Changed -= ApplyTheme;
        ApplyTheme();
    }

    internal void ShowSection(SettingsSection section)
    {
        SelectSection(section);
        if (section == SettingsSection.Sensors) RefreshSensors();
    }

    internal void ApplyTheme()
    {
        BackColor = Theme.Bg;
        _nav.BackColor = Theme.Panel;
        _content.BackColor = Theme.Bg;
        foreach (var (section, nav, page) in _items)
        {
            bool selected = section == _selected;
            nav.BackColor = selected ? Theme.PanelAlt : Theme.Panel;
            nav.ForeColor = selected ? Theme.Text : Theme.Muted;
            nav.FlatAppearance.BorderSize = 0;
            nav.FlatAppearance.MouseOverBackColor = Theme.PanelAlt;
            nav.Font = Theme.FontBold;
            string icon = section switch
            {
                SettingsSection.Export => "file-down",
                SettingsSection.Sensors => "activity",
                SettingsSection.Updates => "refresh-cw",
                SettingsSection.Themes => "monitor",
                _ => "file-text"
            };
            nav.Image = LucideIcons.Get(icon, selected ? Theme.Cpu : Theme.Muted, 18);
            page.BackColor = Theme.Bg;
            if (page is NoticesPanel notices) notices.ApplyTheme();
            if (page is UpdatesPage updates) updates.ApplyTheme();
        }
        _sensorView.BackColor = Theme.Panel;
        _sensorView.ForeColor = Theme.Text;
        _sensorView.Font = Theme.FontMono;
        _exportStatus.ForeColor = Theme.Muted;
        StyleThemeButtons();
    }

    private void AddSection(SettingsSection section, string caption, string icon, Control page)
    {
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        page.BackColor = Theme.Bg;
        _content.Controls.Add(page);

        var nav = new Button
        {
            Text = "  " + caption,
            Width = 200,
            Height = 42,
            Margin = new Padding(0, 0, 0, 6),
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 8, 0),
            Cursor = Cursors.Hand,
            Font = Theme.FontBold
        };
        nav.FlatAppearance.BorderSize = 0;
        nav.Image = LucideIcons.Get(icon, Theme.Muted, 18);
        nav.ImageAlign = ContentAlignment.MiddleLeft;
        nav.TextImageRelation = TextImageRelation.ImageBeforeText;
        nav.Click += (_, _) => ShowSection(section);
        _nav.Controls.Add(nav);
        _items.Add((section, nav, page));
    }

    private void SelectSection(SettingsSection section)
    {
        _selected = section;
        foreach (var (s, _, page) in _items)
            page.Visible = s == section;
        ApplyTheme();
    }

    private Control BuildExportPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24) };
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true
        };
        var title = new Label { Text = "Exportar sesión", AutoSize = true, Font = Theme.FontTitle, Margin = new Padding(0, 0, 0, 8) };
        var info = new Label
        {
            Text = "Crea un ZIP local con métricas, procesos y diagnóstico de esta sesión. No se envía automáticamente; revisa el contenido antes de compartirlo.",
            AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(0, 8, 0, 16)
        };
        var export = new Button { Text = "Exportar ZIP", AutoSize = true, Height = 38, Margin = new Padding(0, 8, 0, 8) };
        Theme.StyleButton(export, accent: true, back: Theme.Gpu);
        LucideIcons.Apply(export, "file-down");
        export.Click += async (_, _) =>
        {
            export.Enabled = false;
            try
            {
                await _exportAsync();
                _exportStatus.Text = "Si confirmaste la exportación, el ZIP quedó en tu carpeta de sesión.";
            }
            finally { export.Enabled = true; }
        };
        _exportStatus = new Label { Text = "", AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(0, 12, 0, 0) };
        layout.Controls.Add(title);
        layout.Controls.Add(info);
        layout.Controls.Add(export);
        layout.Controls.Add(_exportStatus);
        page.Controls.Add(layout);
        return page;
    }

    private Control BuildSensorsPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 16, 24, 16) };
        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 48, ColumnCount = 3 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        var title = new Label { Text = "Sensores detectados", Dock = DockStyle.Fill, Font = Theme.FontBold, TextAlign = ContentAlignment.MiddleLeft };
        var refresh = new Button { Text = "Actualizar", Dock = DockStyle.Fill, Margin = new Padding(4) };
        var copy = new Button { Text = "Copiar diagnóstico", Dock = DockStyle.Fill, Margin = new Padding(4) };
        Theme.StyleButton(refresh);
        Theme.StyleButton(copy);
        LucideIcons.Apply(refresh, "refresh-cw");
        LucideIcons.Apply(copy, "copy");
        refresh.Click += (_, _) => RefreshSensors();
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(_sensorView.Text); }
            catch (Exception e) { MessageBox.Show(e.Message, "Portapapeles", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(refresh, 1, 0);
        header.Controls.Add(copy, 2, 0);
        page.Controls.Add(_sensorView);
        page.Controls.Add(header);
        return page;
    }

    private void RefreshSensors()
    {
        string report = _sensorReport();
        _saveSensorReport(report);
        _sensorView.Text = report;
    }

    private Control BuildThemesPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24) };
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true
        };
        var title = new Label { Text = "Apariencia", AutoSize = true, Font = Theme.FontTitle, Margin = new Padding(0, 0, 0, 8) };
        var info = new Label
        {
            Text = "Automático sigue el tema claro/oscuro de Windows. El modo se guarda solo en este equipo.",
            AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(0, 8, 0, 20)
        };
        layout.Controls.Add(title);
        layout.Controls.Add(info);
        foreach (ThemeMode mode in Enum.GetValues<ThemeMode>())
        {
            var button = new Button
            {
                Text = Theme.ModeLabel(mode),
                Width = 280,
                Height = 40,
                Margin = new Padding(0, 0, 0, 10),
                Tag = mode,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 8, 0),
                Cursor = Cursors.Hand
            };
            button.Click += (_, _) =>
            {
                if (button.Tag is ThemeMode chosen) Theme.SetMode(chosen);
                StyleThemeButtons();
            };
            _themeButtons.Add(button);
            layout.Controls.Add(button);
        }
        page.Controls.Add(layout);
        StyleThemeButtons();
        return page;
    }

    private void StyleThemeButtons()
    {
        foreach (var button in _themeButtons)
        {
            bool selected = button.Tag is ThemeMode mode && mode == Theme.Mode;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = selected ? Theme.Cpu : Theme.PanelAlt;
            button.ForeColor = selected ? Theme.OnAccent : Theme.Text;
            button.Font = Theme.FontBold;
            button.FlatAppearance.MouseOverBackColor = selected ? Theme.Cpu : ControlPaint.Light(Theme.PanelAlt, 0.08f);
        }
    }
}
