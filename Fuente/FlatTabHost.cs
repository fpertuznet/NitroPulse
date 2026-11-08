namespace NitroPulse;

/// <summary>Borderless tab host — no WinForms TabControl chrome (no white frames).</summary>
internal sealed class FlatTabHost : UserControl
{
    private readonly TableLayoutPanel _strip = new() { Dock = DockStyle.Top, Height = 40, ColumnCount = 0, RowCount = 1 };
    private readonly Panel _body = new() { Dock = DockStyle.Fill };
    private readonly List<(Button Tab, Control Page, string Caption)> _pages = new();
    private int _selected;

    public FlatTabHost()
    {
        DoubleBuffered = true;
        BackColor = Theme.Bg;
        _strip.BackColor = Theme.Bg;
        _strip.Padding = new Padding(0);
        _strip.Margin = new Padding(0);
        _strip.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(_body);
        Controls.Add(_strip);
        Theme.Changed += ApplyTheme;
        Disposed += (_, _) => Theme.Changed -= ApplyTheme;
    }

    public int SelectedIndex
    {
        get => _selected;
        set => SelectIndex(value);
    }

    public int Count => _pages.Count;

    public Control? SelectedPage => _selected >= 0 && _selected < _pages.Count ? _pages[_selected].Page : null;

    public void Add(string caption, Control page, string? icon = null)
    {
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        page.BackColor = Theme.Bg;
        _body.Controls.Add(page);

        var tab = new Button
        {
            Text = "  " + caption,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = Theme.FontBold,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0),
            Padding = new Padding(8, 0, 8, 0)
        };
        tab.FlatAppearance.BorderSize = 0;
        tab.FlatAppearance.MouseOverBackColor = Theme.PanelAlt;
        tab.FlatAppearance.MouseDownBackColor = Theme.PanelAlt;
        if (icon is not null)
        {
            tab.Image = LucideIcons.Get(icon, Theme.Muted, 18);
            tab.ImageAlign = ContentAlignment.MiddleLeft;
            tab.TextImageRelation = TextImageRelation.ImageBeforeText;
        }
        int index = _pages.Count;
        tab.Click += (_, _) => SelectIndex(index);
        _strip.ColumnCount = index + 1;
        _strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // Rebalance equal columns
        for (int i = 0; i < _strip.ColumnStyles.Count; i++)
            _strip.ColumnStyles[i] = new ColumnStyle(SizeType.Percent, 100f / _strip.ColumnCount);
        _strip.Controls.Add(tab, index, 0);
        _pages.Add((tab, page, caption));
        if (_pages.Count == 1) SelectIndex(0);
        else StyleTabs();
    }

    public void SelectIndex(int index)
    {
        if (index < 0 || index >= _pages.Count) return;
        _selected = index;
        for (int i = 0; i < _pages.Count; i++)
            _pages[i].Page.Visible = i == index;
        StyleTabs();
    }

    public void ApplyTheme()
    {
        BackColor = Theme.Bg;
        _strip.BackColor = Theme.Bg;
        _body.BackColor = Theme.Bg;
        foreach (var (_, page, _) in _pages)
            page.BackColor = Theme.Bg;
        StyleTabs();
    }

    private void StyleTabs()
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            var (tab, _, caption) = _pages[i];
            bool selected = i == _selected;
            tab.BackColor = selected ? Theme.PanelAlt : Theme.Bg;
            tab.ForeColor = selected ? Theme.Text : Theme.Muted;
            tab.Font = Theme.FontBold;
            tab.FlatAppearance.BorderSize = 0;
            tab.FlatAppearance.MouseOverBackColor = Theme.PanelAlt;
            if (caption is "Recursos" or "Red" or "Configuración" || caption.StartsWith("Privacidad", StringComparison.Ordinal))
            {
                string name = caption == "Recursos" ? "monitor" : caption == "Red" ? "network" : "settings";
                tab.Image = LucideIcons.Get(name, selected ? Theme.Cpu : Theme.Muted, 18);
                tab.TextImageRelation = TextImageRelation.ImageBeforeText;
                tab.ImageAlign = ContentAlignment.MiddleLeft;
            }
            tab.Paint -= DrawUnderline;
            if (selected) tab.Paint += DrawUnderline;
            tab.Invalidate();
        }
    }

    private static void DrawUnderline(object? sender, PaintEventArgs e)
    {
        if (sender is not Button button) return;
        using var pen = new Pen(Theme.Cpu, 2);
        e.Graphics.DrawLine(pen, 16, button.Height - 3, button.Width - 16, button.Height - 3);
    }
}
