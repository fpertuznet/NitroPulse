using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Runtime.InteropServices;

namespace NitroPulse;

internal sealed class MetricCard : Control
{
    private string _header;
    private readonly Color _accent;
    private string _value = "—";
    private string _detail = "Esperando datos";
    private double? _percentage;

    public MetricCard(string header, Color accent)
    {
        _header = header;
        _accent = accent;
        DoubleBuffered = true;
        Margin = new Padding(5);
        MinimumSize = new Size(128, 126);
        BackColor = Theme.Panel;
    }

    public void SetValue(string value, string detail, double? percent)
    {
        _value = value;
        _detail = detail;
        _percentage = percent;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var bg = new SolidBrush(Theme.Panel);
        using var border = new Pen(Theme.Border, 1);
        using var rectPath = Rounded(rect, 13);
        g.FillPath(bg, rectPath);
        g.DrawPath(border, rectPath);
        using var accentBrush = new SolidBrush(_accent);
        g.FillRectangle(accentBrush, 17, 17, 8, 8);
        using var headerFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var bigFont = new Font("Segoe UI Semibold", 24, FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", 8f);
        using var white = new SolidBrush(Theme.Text);
        using var muted = new SolidBrush(Theme.Muted);
        g.DrawString(_header, headerFont, muted, 30, 12);
        g.DrawString(_value, bigFont, white, 14, 38);
        var detailRect = new RectangleF(17, 92, Math.Max(0, Width - 30), 18);
        g.DrawString(_detail, smallFont, muted, detailRect);
        var track = new Rectangle(17, Height - 17, Math.Max(4, Width - 34), 5);
        using var trackBrush = new SolidBrush(Theme.Border);
        g.FillRectangle(trackBrush, track);
        if (_percentage.HasValue)
            g.FillRectangle(accentBrush, track.X, track.Y,
                (int)(track.Width * Math.Clamp(_percentage.Value / 100d, 0, 1)), track.Height);
    }

    public static GraphicsPath Rounded(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(rectangle.X, rectangle.Y, d, d, 180, 90);
        path.AddArc(rectangle.Right - d, rectangle.Y, d, d, 270, 90);
        path.AddArc(rectangle.Right - d, rectangle.Bottom - d, d, d, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class HistoryPlot : Control
{
    private readonly PlotNavigation _navigation = new(300);
    private readonly bool _temperatures;
    private readonly List<MetricSnapshot> _history;
    public HistoryPlot(bool temperatures, List<MetricSnapshot> history)
    {
        _temperatures = temperatures;
        _history = history;
        DoubleBuffered = true;
        Margin = new Padding(5);
        BackColor = Theme.Panel;
        Controls.Add(_navigation); _navigation.Bind(this);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var bg = new SolidBrush(Theme.Panel);
        using var border = new Pen(Theme.Border);
        using var panelPath = MetricCard.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 13);
        g.FillPath(bg, panelPath);
        g.DrawPath(border, panelPath);
        if (Width < 200 || Height < 140) return;
        _navigation.SetTimes(_history.Select(s => s.Timestamp.Ticks).ToArray());
        var samples = _history.Skip(_navigation.View.Start).Take(_navigation.View.Count).ToArray();

        using var titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var tiny = new Font("Segoe UI", 8f);
        using var textBrush = new SolidBrush(Theme.Text);
        using var muted = new SolidBrush(Theme.Muted);
        g.DrawString(_temperatures ? "Temperaturas" : "Uso de recursos", titleFont, textBrush, 16, 12);
        int keyX = _temperatures ? 160 : 156;
        if (_temperatures)
        {
            Key("CPU", Theme.CpuTemp);
            Key("GPU", Theme.GpuTemp);
        }
        else
        {
            Key("CPU", Theme.Cpu);
            Key("GPU", Theme.Gpu);
            Key("RAM", Theme.Ram);
        }
        void Key(string label, Color accent)
        {
            using var dot = new SolidBrush(accent);
            g.FillEllipse(dot, keyX, 20, 7, 7);
            g.DrawString(label, tiny, muted, keyX + 10, 14);
            keyX += 61;
        }

        var area = new RectangleF(42, 47, Width - 57, Height - 84 - _navigation.Height);
        const float maxPercent = 100f;
        float topValue = _temperatures ? 110f : maxPercent;
        using var gridPen = new Pen(Theme.Border, 1);
        for (int i = 0; i <= 4; i++)
        {
            float y = area.Bottom - area.Height * i / 4;
            g.DrawLine(gridPen, area.Left, y, area.Right, y);
            float label = topValue * i / 4;
            g.DrawString(_temperatures ? $"{label:0}°" : $"{label:0}%", tiny, muted, 6, y - 7);
        }

        if (samples.Length > 1)
        {
            var start = samples[0].Timestamp;
            var end = samples[^1].Timestamp;
            double seconds = Math.Max(1, (end - start).TotalSeconds);
            void DrawSeries(Func<MetricSnapshot, double?> selector, Color color)
            {
                using var pen = new Pen(color, 2.2f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                PointF? last = null;
                for (int i = 0; i < samples.Length; i++)
                {
                    var yValue = selector(samples[i]);
                    if (yValue is null) { last = null; continue; }
                    var x = area.Left + (float)((samples[i].Timestamp - start).TotalSeconds / seconds) * area.Width;
                    var y = area.Bottom - (float)(Math.Clamp(yValue.Value, 0, topValue) / topValue) * area.Height;
                    var now = new PointF(x, y);
                    if (last.HasValue && i > 0 && (samples[i].Timestamp - samples[i - 1].Timestamp).TotalSeconds is >= 0 and <= 6) g.DrawLine(pen, last.Value, now);
                    last = now;
                }
            }
            if (_temperatures)
            {
                DrawSeries(s => s.CpuTemperature, Theme.CpuTemp);
                DrawSeries(s => s.GpuTemperature, Theme.GpuTemp);
            }
            else
            {
                DrawSeries(s => s.CpuPercent, Theme.Cpu);
                DrawSeries(s => s.GpuPercent, Theme.Gpu);
                DrawSeries(s => s.RamPercent, Theme.Ram);
            }
            for (int i = 0; i <= 4; i++)
            {
                var at = start.AddSeconds(seconds * i / 4);
                g.DrawString(at.ToString("HH:mm"), tiny, muted, area.Left + area.Width * i / 4 - 14, area.Bottom + 3);
            }
        }
        string state = _navigation.View.IsLive ? "En vivo" : "Historial";
        string range = samples.Length == 0 ? "Esperando datos" : $"{samples[0].Timestamp:dd/MM HH:mm} — {samples[^1].Timestamp:dd/MM HH:mm}";
        using var footerFormat = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString($"{state} · {range} · Arrastra / rueda", tiny, muted,
            new RectangleF(16, Height - _navigation.Height - 18, Math.Max(1, Width - 32), 16), footerFormat);
    }
}

internal enum TrayMetricKind
{
    TemperatureNumber,
    VerticalBar
}

/// <summary>
/// Small tray icons render best with exact Windows taskbar sizes in the .ico.
/// Every bar paints the entire square from x=0 to x=size, with the fill
/// going from bottom to top. Temperature icons have no background, border,
/// margins, percent sign, or degree symbol; their tooltip carries the unit.
/// </summary>
internal static class TrayIconRenderer
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static readonly int[] Resolutions = { 16, 20, 24, 32, 40, 48, 64 };
    private static readonly Color CpuDark = Color.FromArgb(12, 55, 25);
    private static readonly Color GpuDark = Color.FromArgb(76, 15, 22);
    private static readonly Color RamDark = Color.FromArgb(67, 37, 18);

    public static Icon Create(string value, Color accent, TrayMetricKind kind, double? percentage = null)
    {
        byte[][] frames = new byte[Resolutions.Length][];
        for (int i = 0; i < Resolutions.Length; i++)
        {
            int size = Resolutions[i];
            using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                if (kind == TrayMetricKind.VerticalBar)
                    DrawBar(g, size, accent, percentage);
                else
                    DrawTemperature(g, size, accent, value);
            }
            using var png = new MemoryStream();
            bmp.Save(png, ImageFormat.Png);
            frames[i] = png.ToArray();
        }

        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0); // reserved
            writer.Write((ushort)1); // icon
            writer.Write((ushort)frames.Length);
            int offset = 6 + frames.Length * 16;
            for (int i = 0; i < frames.Length; i++)
            {
                writer.Write((byte)Resolutions[i]);
                writer.Write((byte)Resolutions[i]);
                writer.Write((byte)0); // palette size
                writer.Write((byte)0); // reserved
                writer.Write((ushort)1); // planes
                writer.Write((ushort)32); // bits per pixel
                writer.Write((uint)frames[i].Length);
                writer.Write((uint)offset);
                offset += frames[i].Length;
            }
            foreach (var frame in frames) writer.Write(frame);
        }
        ico.Position = 0;
        try
        {
            using var icon = new Icon(ico, SystemInformation.SmallIconSize);
            return (Icon)icon.Clone();
        }
        catch (ArgumentException)
        {
            // Legacy fallback if a Windows icon decoder rejects PNG ICO frames.
            // Even in this case there are no margins, text or outlines on bars.
            using var bmp = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                if (kind == TrayMetricKind.VerticalBar) DrawBar(g, 64, accent, percentage);
                else DrawTemperature(g, 64, accent, value);
            }
            IntPtr handle = bmp.GetHicon();
            try
            {
                using var fallbackIcon = Icon.FromHandle(handle);
                return (Icon)fallbackIcon.Clone();
            }
            finally { DestroyIcon(handle); }
        }
    }

    internal static void DrawBar(Graphics g, int size, Color bright, double? percentage)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        if (percentage is double saturated && double.IsFinite(saturated) && saturated >= 100)
        {
            // Exact threshold, without rounding: 99.99% must remain a bar. The glyph replaces it.
            g.Clear(Color.Transparent);
            using var warning = new SolidBrush(bright);
            int width = Math.Max(3, (int)Math.Round(size * .24));
            int dot = Math.Max(2, (int)Math.Round(size * .18));
            int gap = Math.Max(2, (int)Math.Round(size * .10));
            int x = (size - width) / 2;
            g.FillRectangle(warning, x, 0, width, size - dot - gap);
            g.FillRectangle(warning, x, size - dot, width, dot);
            return;
        }
        Color dark = bright == Theme.Cpu ? CpuDark : bright == Theme.Gpu ? GpuDark : RamDark;
        using var background = new SolidBrush(dark);
        using var fill = new SolidBrush(bright);
        int borderWidth = size <= 20 ? 1 : 2;
        using var border = new SolidBrush(Color.FromArgb(210, 218, 230));
        int innerSize = size - 2 * borderWidth;
        g.FillRectangle(background, 0, 0, size, size);
        if (percentage.HasValue)
        {
            // Quantize downward inside the border: only 100% may appear completely full.
            double percent = double.IsFinite(percentage.Value) ? Math.Clamp(percentage.Value, 0, 100) : 0;
            int used = (int)Math.Floor(innerSize * percent / 100d);
            if (used > 0) g.FillRectangle(fill, borderWidth, borderWidth + innerSize - used, innerSize, used);
        }
        g.FillRectangle(border, 0, 0, size, borderWidth);
        g.FillRectangle(border, 0, size - borderWidth, size, borderWidth);
        g.FillRectangle(border, 0, 0, borderWidth, size);
        g.FillRectangle(border, size - borderWidth, 0, borderWidth, size);
    }

    internal static string CacheKey(TrayMetricKind kind, double? value, string text)
    {
        if (kind == TrayMetricKind.TemperatureNumber) return text;
        if (value is not double percent || !double.IsFinite(percent)) return "bar:unknown";
        if (percent >= 100) return "!";
        // Rebuild only when a frame's pixel height changes, but never merge 99.x with saturation.
        return "bar:" + string.Join(',', Resolutions.Select(size =>
            (int)Math.Floor((size - 2 * (size <= 20 ? 1 : 2)) * Math.Clamp(percent, 0, 100) / 100)));
    }

    internal static string PercentText(double value) =>
        (value is >= 0 and < 100 ? Math.Floor(value * 100) / 100 : value).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static void DrawTemperature(Graphics g, int size, Color accent, string value)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        string glyph = value;
        if (int.TryParse(value, out int temperature))
            glyph = temperature >= 100 ? "HI" : temperature < 0 ? "--" : temperature.ToString("00");

        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        using var shape = new GraphicsPath();
        shape.AddString(glyph, FontFamily.GenericSansSerif, (int)FontStyle.Bold,
            72f, new PointF(0, 0), format);
        var bounds = shape.GetBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        float targetW = size * 0.98f;
        float targetH = size * 0.84f;
        float scale = Math.Min(targetW / bounds.Width, targetH / bounds.Height);
        float offsetX = (size - bounds.Width * scale) / 2f - bounds.X * scale;
        float offsetY = (size - bounds.Height * scale) / 2f - bounds.Y * scale;
        using var transform = new Matrix(scale, 0, 0, scale, offsetX, offsetY);
        shape.Transform(transform);
        using var brush = new SolidBrush(accent);
        g.FillPath(brush, shape);
    }
}
