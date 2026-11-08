using System.Drawing.Drawing2D;

namespace NitroPulse;

internal static class LucideIcons
{
    private static readonly Dictionary<string, Bitmap> Cache = new();
    private static readonly object Gate = new();

    public static Image Get(string name, Color? color = null, int size = 20)
    {
        Color foreground = color ?? Theme.Text;
        string key = name + ":" + foreground.ToArgb() + ":" + size;
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
        }
        using var stream = typeof(LucideIcons).Assembly.GetManifestResourceStream("NitroPulse.Lucide." + name + ".png")
            ?? throw new InvalidOperationException("Icono Lucide no incluido: " + name);
        using var source = new Bitmap(stream);
        using var tinted = new Bitmap(source.Width, source.Height);
        for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++)
            tinted.SetPixel(x, y, Color.FromArgb(source.GetPixel(x, y).A, foreground));
        var result = new Bitmap(size, size);
        using (var g = Graphics.FromImage(result)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(tinted, new Rectangle(0, 0, size, size)); }
        lock (Gate) { Cache[key] = result; }
        return result;
    }

    public static void ClearCache()
    {
        lock (Gate)
        {
            foreach (var bitmap in Cache.Values) bitmap.Dispose();
            Cache.Clear();
        }
    }

    public static void Apply(Button button, string icon)
    {
        button.Image = Get(icon, button.ForeColor, 18);
        button.ImageAlign = ContentAlignment.MiddleLeft;
        button.TextImageRelation = TextImageRelation.ImageBeforeText;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Padding = new Padding(10, 0, 8, 0);
        button.UseCompatibleTextRendering = false;
    }

    public static string ForText(string text) => text switch
    {
        var t when t.StartsWith("Pausar") => "pause",
        var t when t.StartsWith("Reanudar") => "play",
        var t when t.StartsWith("Exportar") => "file-down",
        var t when t.StartsWith("Copiar") => "copy",
        var t when t.StartsWith("Ver sensores") => "activity",
        var t when t.StartsWith("Actualizaci") => "refresh-cw",
        var t when t.StartsWith("Guardar") => "tag",
        var t when t.StartsWith("Medir") => "route",
        var t when t.StartsWith("Cerrar") => "x",
        var t when t.StartsWith("Apariencia") => "settings",
        var t when t.StartsWith("Configuración") => "settings",
        _ => "settings"
    };
}
