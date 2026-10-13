using System.Drawing.Drawing2D;

namespace NitroPulse;

internal static class LucideIcons
{
    private static readonly Dictionary<string, Bitmap> Cache = new();
    public static Image Get(string name, Color? color = null, int size = 20)
    {
        Color foreground = color ?? Theme.Text;
        string key = name + ":" + foreground.ToArgb() + ":" + size;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        using var stream = typeof(LucideIcons).Assembly.GetManifestResourceStream("NitroPulse.Lucide." + name + ".png")
            ?? throw new InvalidOperationException("Icono Lucide no incluido: " + name);
        using var source = new Bitmap(stream);
        using var tinted = new Bitmap(source.Width, source.Height);
        for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++)
            tinted.SetPixel(x, y, Color.FromArgb(source.GetPixel(x, y).A, foreground));
        var result = new Bitmap(size, size);
        using (var g = Graphics.FromImage(result)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(tinted, new Rectangle(0, 0, size, size)); }
        Cache[key] = result; return result;
    }
    public static void Apply(Button button, string icon)
    {
        button.Image = Get(icon, button.ForeColor);
        button.ImageAlign = ContentAlignment.MiddleLeft; button.TextImageRelation = TextImageRelation.ImageBeforeText;
        button.TextAlign = ContentAlignment.MiddleCenter; button.Padding = new Padding(6, 0, 6, 0);
    }
    public static string ForText(string text) => text switch
    {
        var t when t.StartsWith("Pausar") => "pause",
        var t when t.StartsWith("Reanudar") => "play",
        var t when t.StartsWith("Exportar") => "file-down",
        var t when t.StartsWith("Copiar") => "copy",
        var t when t.StartsWith("Ver sensores") => "activity",
        var t when t.StartsWith("Actualizaci") => "download",
        var t when t.StartsWith("Guardar") => "tag",
        var t when t.StartsWith("Medir") => "route",
        var t when t.StartsWith("Cerrar") => "x",
        _ => "settings"
    };
}

internal static class NitroTabs
{
    public static TabControl Create()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed, ItemSize = new Size(160, 34), SizeMode = TabSizeMode.Fixed };
        bool fitting = false;
        void FitHeaders()
        {
            if (fitting || tabs.TabPages.Count == 0) return;
            var size = new Size(Math.Max(80, (tabs.ClientSize.Width - 8) / tabs.TabPages.Count), tabs.ItemSize.Height);
            if (size == tabs.ItemSize) return;
            fitting = true;
            try { tabs.ItemSize = size; }
            finally { fitting = false; }
        }
        tabs.SizeChanged += (_, _) => FitHeaders(); tabs.ControlAdded += (_, _) => FitHeaders();
        tabs.DrawItem += (_, e) =>
        {
            bool selected = e.Index == tabs.SelectedIndex;
            using var background = new SolidBrush(selected ? Theme.PanelAlt : Theme.Bg);
            e.Graphics.FillRectangle(background, e.Bounds);
            string caption = tabs.TabPages[e.Index].Text;
            string? icon = caption == "Recursos" ? "monitor" : caption == "Red" ? "network" : null;
            var textBounds = e.Bounds;
            if (icon is not null)
            {
                int width = TextRenderer.MeasureText(caption, tabs.Font).Width;
                int x = e.Bounds.Left + Math.Max(8, (e.Bounds.Width - width - 26) / 2);
                e.Graphics.DrawImage(LucideIcons.Get(icon, selected ? Theme.Cpu : Theme.Text), x, e.Bounds.Top + (e.Bounds.Height - 20) / 2);
                textBounds.X = x + 26; textBounds.Width = width;
            }
            TextRenderer.DrawText(e.Graphics, caption, tabs.Font, textBounds, selected ? Theme.Cpu : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (selected) { using var pen = new Pen(Theme.Cpu, 2); e.Graphics.DrawLine(pen, e.Bounds.Left + 8, e.Bounds.Bottom - 2, e.Bounds.Right - 8, e.Bounds.Bottom - 2); }
        };
        return tabs;
    }
}
