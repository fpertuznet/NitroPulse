using System.Text.Json;
using Microsoft.Win32;

namespace NitroPulse;

internal enum ThemeMode { Auto, Light, Dark }

internal enum MetricAccent { Cpu, Gpu, Ram, CpuTemp, GpuTemp }

internal readonly record struct ThemePalette(
    Color Bg, Color Panel, Color PanelAlt, Color Border, Color Text, Color Muted,
    Color Cpu, Color Gpu, Color Ram, Color Selection, Color OnAccent, Color Grid);

/// <summary>
/// Runtime theme: Auto follows Windows AppsUseLightTheme; Light/Dark are forced.
/// Colors and fonts are resolved once per change — paint paths must not allocate fonts.
/// </summary>
internal static class Theme
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NitroPulse", "ui-settings.json");

    // Dark gaming: near-black blue, vivid accents. Borders stay tonal (almost invisible).
    private static readonly ThemePalette Dark = new(
        Bg: Color.FromArgb(10, 12, 18),
        Panel: Color.FromArgb(20, 24, 34),
        PanelAlt: Color.FromArgb(28, 34, 48),
        Border: Color.FromArgb(28, 34, 46),
        Text: Color.FromArgb(240, 244, 252),
        Muted: Color.FromArgb(140, 152, 176),
        Cpu: Color.FromArgb(46, 232, 126),
        Gpu: Color.FromArgb(255, 64, 96),
        Ram: Color.FromArgb(255, 176, 64),
        Selection: Color.FromArgb(40, 52, 78),
        OnAccent: Color.FromArgb(8, 10, 16),
        Grid: Color.FromArgb(28, 34, 46));

    // Light gaming: soft panels without harsh outlines.
    private static readonly ThemePalette Light = new(
        Bg: Color.FromArgb(241, 243, 248),
        Panel: Color.FromArgb(255, 255, 255),
        PanelAlt: Color.FromArgb(234, 238, 246),
        Border: Color.FromArgb(228, 232, 240),
        Text: Color.FromArgb(18, 22, 34),
        Muted: Color.FromArgb(90, 102, 124),
        Cpu: Color.FromArgb(16, 168, 88),
        Gpu: Color.FromArgb(220, 40, 72),
        Ram: Color.FromArgb(200, 120, 24),
        Selection: Color.FromArgb(214, 222, 238),
        OnAccent: Color.FromArgb(255, 255, 255),
        Grid: Color.FromArgb(228, 232, 240));

    private static ThemePalette _current = Dark;
    private static ThemeMode _mode = ThemeMode.Auto;
    private static bool _initialized;
    private static Font? _body, _title, _value, _small, _bold, _mono;

    public static event Action? Changed;

    public static ThemeMode Mode => _mode;
    public static bool IsDark => _current.Bg.GetBrightness() < 0.45f;
    public static Color Bg => _current.Bg;
    public static Color Panel => _current.Panel;
    public static Color PanelAlt => _current.PanelAlt;
    public static Color Border => _current.Border;
    public static Color Text => _current.Text;
    public static Color Muted => _current.Muted;
    public static Color Cpu => _current.Cpu;
    public static Color Gpu => _current.Gpu;
    public static Color Ram => _current.Ram;
    public static Color CpuTemp => _current.Cpu;
    public static Color GpuTemp => _current.Gpu;
    public static Color Selection => _current.Selection;
    public static Color OnAccent => _current.OnAccent;
    public static Color Grid => _current.Grid;

    public static Font FontBody => _body ?? SystemFonts.MessageBoxFont!;
    public static Font FontTitle => _title ?? SystemFonts.CaptionFont!;
    public static Font FontValue => _value ?? SystemFonts.CaptionFont!;
    public static Font FontSmall => _small ?? SystemFonts.StatusFont!;
    public static Font FontBold => _bold ?? SystemFonts.DefaultFont;
    public static Font FontMono => _mono ?? SystemFonts.DefaultFont;

    public static Color Accent(MetricAccent kind) => kind switch
    {
        MetricAccent.Gpu or MetricAccent.GpuTemp => Gpu,
        MetricAccent.Ram => Ram,
        _ => Cpu
    };

    public static Color[] TrayAccents => [CpuTemp, GpuTemp, Cpu, Gpu, Ram];

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        EnsureFonts();
        _mode = LoadMode();
        Resolve(raise: false);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public static void SetMode(ThemeMode mode)
    {
        if (_mode == mode && _initialized) { Resolve(raise: true); return; }
        _mode = mode;
        SaveMode(mode);
        Resolve(raise: true);
    }

    public static string ModeLabel(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => "Claro",
        ThemeMode.Dark => "Oscuro",
        _ => "Automático (Windows)"
    };

    public static void ApplyControlTree(Control root)
    {
        root.BackColor = Bg;
        root.ForeColor = Text;
        ApplyRecursive(root);
    }

    public static void StyleButton(Button button, bool accent = false, Color? back = null)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.BorderColor = PanelAlt;
        button.FlatAppearance.MouseOverBackColor = accent || back.HasValue ? ControlPaint.Light(back ?? Cpu, 0.08f) : ControlPaint.Light(PanelAlt, 0.06f);
        button.FlatAppearance.MouseDownBackColor = accent || back.HasValue ? ControlPaint.Dark(back ?? Cpu, 0.06f) : ControlPaint.Dark(PanelAlt, 0.04f);
        button.BackColor = back ?? (accent ? Cpu : PanelAlt);
        button.ForeColor = accent || (back.HasValue && IsAccentBack(back.Value)) ? OnAccent : Text;
        button.Font = FontBold;
        button.Cursor = Cursors.Hand;
        if (!string.IsNullOrEmpty(button.Text))
            LucideIcons.Apply(button, LucideIcons.ForText(button.Text));
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Panel;
        grid.GridColor = Grid;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.None;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = PanelAlt;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = PanelAlt;
        grid.ColumnHeadersDefaultCellStyle.Font = FontBold;
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(10, 0, 10, 0);
        grid.DefaultCellStyle.BackColor = Panel;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Selection;
        grid.DefaultCellStyle.SelectionForeColor = Text;
        grid.DefaultCellStyle.Font = FontBody;
        grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.DefaultCellStyle.Padding = new Padding(10, 0, 10, 0);
    }

    public static void StyleMenu(ContextMenuStrip menu)
    {
        menu.BackColor = Panel;
        menu.ForeColor = Text;
        menu.Renderer = new ThemeMenuRenderer();
        foreach (ToolStripItem item in menu.Items)
            StyleMenuItem(item);
    }

    public static void StyleMenuItem(ToolStripItem item)
    {
        item.ForeColor = Text;
        item.BackColor = Panel;
        if (item is ToolStripMenuItem menu)
        {
            foreach (ToolStripItem child in menu.DropDownItems)
                StyleMenuItem(child);
        }
    }

    private static bool IsAccentBack(Color c) =>
        c.ToArgb() == Cpu.ToArgb() || c.ToArgb() == Gpu.ToArgb() || c.ToArgb() == Ram.ToArgb();

    private static void ApplyRecursive(Control control)
    {
        switch (control)
        {
            case MetricCard or HistoryPlot or NetworkPlot or PlotNavigation:
                control.Invalidate();
                break;
            case DataGridView grid:
                StyleGrid(grid);
                break;
            case Button button:
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.BorderColor = PanelAlt;
                break;
            case TextBox box:
                box.BackColor = Panel;
                box.ForeColor = Text;
                break;
            case Label label:
                if (label.ForeColor.ToArgb() == Color.Black.ToArgb() || label.ForeColor.ToArgb() == Color.White.ToArgb())
                    label.ForeColor = Text;
                break;
            case TabPage page:
                page.BackColor = Bg;
                page.ForeColor = Text;
                break;
            case TabControl tabs:
                tabs.BackColor = Bg;
                tabs.Invalidate();
                break;
            case System.Windows.Forms.Panel or TableLayoutPanel or FlowLayoutPanel or UserControl:
                control.BackColor = Bg;
                control.ForeColor = Text;
                break;
        }
        foreach (Control child in control.Controls)
            ApplyRecursive(child);
    }

    private static void Resolve(bool raise)
    {
        bool wantLight = _mode switch
        {
            ThemeMode.Light => true,
            ThemeMode.Dark => false,
            _ => WindowsAppsUseLightTheme()
        };
        _current = wantLight ? Light : Dark;
        LucideIcons.ClearCache();
        if (raise) Changed?.Invoke();
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_mode != ThemeMode.Auto) return;
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Color))
            return;
        var previous = _current;
        Resolve(raise: false);
        if (previous.Bg.ToArgb() != _current.Bg.ToArgb())
            Changed?.Invoke();
    }

    private static bool WindowsAppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("AppsUseLightTheme");
            if (value is int i) return i != 0;
            if (value is long l) return l != 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Fall through to dark (monitor default).
        }
        return false;
    }

    private static ThemeMode LoadMode()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return ThemeMode.Auto;
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            if (doc.RootElement.TryGetProperty("mode", out var mode) &&
                Enum.TryParse<ThemeMode>(mode.GetString(), ignoreCase: true, out var parsed))
                return parsed;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
        }
        return ThemeMode.Auto;
    }

    private static void SaveMode(ThemeMode mode)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { mode = mode.ToString() }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void EnsureFonts()
    {
        _body = MakeFont(["Segoe UI", "Segoe UI Variable Text"], 9.5f, FontStyle.Regular);
        _bold = MakeFont(["Segoe UI Semibold", "Segoe UI", "Segoe UI Variable Text"], 9f, FontStyle.Bold);
        _title = MakeFont(["Bahnschrift", "Segoe UI Semibold", "Segoe UI"], 20f, FontStyle.Bold);
        _value = MakeFont(["Bahnschrift", "Segoe UI Semibold", "Segoe UI"], 22f, FontStyle.Bold);
        _small = MakeFont(["Segoe UI", "Segoe UI Variable Text"], 8f, FontStyle.Regular);
        _mono = MakeFont(["Cascadia Mono", "Consolas", "Courier New"], 9f, FontStyle.Regular);
    }

    private static Font MakeFont(string[] families, float size, FontStyle style)
    {
        foreach (string family in families)
        {
            try
            {
                using var probe = new Font(family, size, style, GraphicsUnit.Point);
                if (string.Equals(probe.FontFamily.Name, family, StringComparison.OrdinalIgnoreCase) ||
                    probe.Name.Contains(family.Split(' ')[0], StringComparison.OrdinalIgnoreCase))
                    return new Font(probe.FontFamily, size, style, GraphicsUnit.Point);
            }
            catch (ArgumentException) { }
        }
        return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
    }

    private sealed class ThemeMenuRenderer : ToolStripProfessionalRenderer
    {
        public ThemeMenuRenderer() : base(new ThemeColorTable()) { }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Text : Text;
            base.OnRenderItemText(e);
        }
    }

    private sealed class ThemeColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Selection;
        public override Color MenuItemSelectedGradientBegin => Selection;
        public override Color MenuItemSelectedGradientEnd => Selection;
        public override Color MenuItemBorder => Border;
        public override Color MenuBorder => Border;
        public override Color ToolStripDropDownBackground => Panel;
        public override Color ImageMarginGradientBegin => Panel;
        public override Color ImageMarginGradientMiddle => Panel;
        public override Color ImageMarginGradientEnd => Panel;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
    }
}
